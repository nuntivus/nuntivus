using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NuntivusServer.Application.Deliveries.Recovery;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common.Logging;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Domain.Deliveries;
using NuntivusServer.Infastructure.Persistence.Postgres;
using Npgsql;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(DeliveriesCollection))]
    public sealed class DeliveryRecoveryServiceTests : IAsyncLifetime
    {
        private static readonly DeliveryFactory DeliveryFactory = new();

        private readonly DeliveriesFixture _fx;

        public DeliveryRecoveryServiceTests(DeliveriesFixture fx) => _fx = fx;

        public Task InitializeAsync() => _fx.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task ManualRetryAsync_Resets_AttemptCount_And_Is_Idempotent()
        {
            var delivery = await SeedDeadDeliveryAsync();
            var service = BuildService();
            var request = NewRequest("manual retry");

            var first = await service.ManualRetryAsync(delivery.Id, request, CancellationToken.None);
            var second = await service.ManualRetryAsync(delivery.Id, request, CancellationToken.None);

            first.EnsureSuccess();
            second.EnsureSuccess();

            Assert.Equal(DeliveryStatus.Pending, first.Value.Status);
            Assert.Equal(0, first.Value.AttemptCount);
            Assert.NotNull(first.Value.NextAttemptAt);
            Assert.Equal(DeliveryStatus.Pending, second.Value.Status);
            Assert.Equal(0, second.Value.AttemptCount);
            Assert.NotNull(second.Value.NextAttemptAt);
            Assert.InRange(
                (first.Value.NextAttemptAt.Value - second.Value.NextAttemptAt.Value).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));

            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            var repo = new PostgresDeliveryRepository(factory);
            using (var uow = new PostgresUnitOfWork(factory))
            {
                uow.Begin(true);
                var due = await repo.ClaimDueForSchedulingAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10, uow, CancellationToken.None);
                due.EnsureSuccess();
                Assert.Single(due.Value);
                Assert.Equal(delivery.Id, due.Value[0].Id);
                uow.Commit();
            }

            using (var uow = new PostgresUnitOfWork(factory))
            {
                uow.Begin(true);
                var dueAgain = await repo.ClaimDueForSchedulingAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10, uow, CancellationToken.None);
                dueAgain.EnsureSuccess();
                Assert.Empty(dueAgain.Value);
                uow.Commit();
            }

            var attempts = await ReadAttemptsAsync(delivery.Id);
            Assert.Single(attempts);
            Assert.Equal(1, await ReadAuditCountAsync(delivery.Id));
        }

        [Fact]
        public async Task ManualRetryAsync_Reschedules_Previously_Attempted_Pending_Delivery()
        {
            var delivery = await SeedDeadDeliveryAsync();
            var service = BuildService();
            (await service.ManualRequeueAsync(delivery.Id, NewRequest("requeue"), CancellationToken.None)).EnsureSuccess();

            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using (var uow = new PostgresUnitOfWork(factory))
            {
                uow.Begin(true);
                var claimed = await new PostgresDeliveryRepository(factory).ClaimDueForSchedulingAsync(
                    DateTimeOffset.UtcNow.AddMinutes(1), 10, uow, CancellationToken.None);
                Assert.Single(claimed.EnsureValue());
                uow.Commit();
            }

            var result = await service.ManualRetryAsync(delivery.Id, NewRequest("retry pending"), CancellationToken.None);
            result.EnsureSuccess();
            Assert.Equal(DeliveryStatus.Pending, result.Value.Status);
            Assert.Equal(0, result.Value.AttemptCount);
            Assert.NotNull(result.Value.NextAttemptAt);
        }

        [Fact]
        public async Task ManualRequeueAsync_Preserves_AttemptCount_And_Is_Idempotent()
        {
            var delivery = await SeedDeadDeliveryAsync();
            var service = BuildService();
            var request = NewRequest("manual requeue");

            var first = await service.ManualRequeueAsync(delivery.Id, request, CancellationToken.None);
            var second = await service.ManualRequeueAsync(delivery.Id, request, CancellationToken.None);

            first.EnsureSuccess();
            second.EnsureSuccess();

            Assert.Equal(DeliveryStatus.Failed, first.Value.Status);
            Assert.Equal(1, first.Value.AttemptCount);
            Assert.NotNull(first.Value.NextAttemptAt);

            Assert.Equal(DeliveryStatus.Failed, second.Value.Status);
            Assert.Equal(1, second.Value.AttemptCount);
            Assert.NotNull(second.Value.NextAttemptAt);

            var attempts = await ReadAttemptsAsync(delivery.Id);
            Assert.Single(attempts);
            Assert.Equal(1, await ReadAuditCountAsync(delivery.Id));
        }

        [Fact]
        public async Task ManualCancelAsync_Transitions_To_Dead_And_Writes_Audit()
        {
            var delivery = await SeedFailedDeliveryAsync();
            var service = BuildService();

            var result = await service.ManualCancelAsync(
                delivery.Id,
                NewRequest("operator cancel"),
                CancellationToken.None);

            result.EnsureSuccess();
            Assert.Equal(DeliveryStatus.Dead, result.Value.Status);
            Assert.Contains("operator cancel", result.Value.LastError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await ReadAuditCountAsync(delivery.Id));
        }

        [Fact]
        public async Task GetFailure_And_Attempts_Return_Inspection_Data()
        {
            var delivery = await SeedDeadDeliveryAsync();
            var service = BuildService();

            var failure = await service.GetFailureAsync(delivery.Id, CancellationToken.None);
            var attempts = await service.GetAttemptsAsync(delivery.Id, CancellationToken.None);

            failure.EnsureSuccess();
            attempts.EnsureSuccess();

            Assert.Equal(delivery.Id, failure.Value.DeliveryId);
            Assert.Equal(DeliveryStatus.Dead, failure.Value.Status);
            Assert.Single(attempts.Value);
            Assert.False(attempts.Value[0].Succeeded);
        }

        [Fact]
        public async Task ListDeadAsync_Returns_Dead_Deliveries_Page()
        {
            var dead = await SeedDeadDeliveryAsync();
            var service = BuildService();

            var page = await service.ListDeadAsync(1, 10, CancellationToken.None);
            page.EnsureSuccess();

            Assert.Contains(page.Value.Items, d => d.Id == dead.Id && d.Status == DeliveryStatus.Dead);
        }

        [Fact]
        public async Task ReplayJob_Submit_Is_Idempotent_And_Replays_Filtered_Failed_Deliveries()
        {
            var now = DateTimeOffset.UtcNow;
            var matchingId = await SeedReplayDeliveryAsync(DeliveryStatus.Dead, "orders.created", now.AddMinutes(-5));
            var nonMatchingId = await SeedReplayDeliveryAsync(DeliveryStatus.Dead, "orders.updated", now.AddMinutes(-5));
            var service = BuildService();

            var operationId = Guid.NewGuid();
            var request = new DeliveryReplayRequest(
                operationId,
                "operator-replay",
                "{\"source\":\"integration\"}",
                EventType: "orders.created",
                CustomerId: null,
                ContractId: null,
                CreatedFrom: now.AddHours(-1),
                CreatedTo: now,
                BatchLimit: 10);

            var first = await service.SubmitReplayJobAsync(request, CancellationToken.None);
            var second = await service.SubmitReplayJobAsync(request, CancellationToken.None);

            first.EnsureSuccess();
            second.EnsureSuccess();
            Assert.Equal(first.Value.Id, second.Value.Id);
            Assert.Equal(DeliveryReplayJobStatus.Pending, first.Value.Status);

            var processed = await service.ProcessReplayJobsAsync(5, CancellationToken.None);
            processed.EnsureSuccess();

            Assert.Equal(1, processed.Value.ClaimedJobs);
            Assert.Equal(1, processed.Value.CompletedJobs);
            Assert.Equal(0, processed.Value.FailedJobs);
            Assert.Equal(1, processed.Value.TotalReplayedDeliveries);

            var completed = await service.GetReplayJobAsync(first.Value.Id, CancellationToken.None);
            completed.EnsureSuccess();
            Assert.Equal(DeliveryReplayJobStatus.Completed, completed.Value.Status);
            Assert.Equal(1, completed.Value.ReplayedCount);

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();

            var matching = await conn.QuerySingleAsync<(string status, DateTimeOffset? next_attempt_at)>(
                "SELECT status, next_attempt_at FROM deliveries WHERE id = @id;",
                new { id = matchingId });
            var nonMatching = await conn.QuerySingleAsync<(string status, DateTimeOffset? next_attempt_at)>(
                "SELECT status, next_attempt_at FROM deliveries WHERE id = @id;",
                new { id = nonMatchingId });

            Assert.Equal("Failed", matching.status);
            Assert.NotNull(matching.next_attempt_at);
            Assert.Equal("Dead", nonMatching.status);
            Assert.Null(nonMatching.next_attempt_at);
        }

        [Fact]
        public async Task ReplayJob_Submit_Rejects_Request_Without_Filters()
        {
            var service = BuildService();

            var result = await service.SubmitReplayJobAsync(
                new DeliveryReplayRequest(
                    Guid.NewGuid(),
                    "operator-replay",
                    "{\"source\":\"integration\"}",
                    EventType: null,
                    CustomerId: null,
                    ContractId: null,
                    CreatedFrom: null,
                    CreatedTo: null,
                    BatchLimit: 10),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("validation", result.Error?.Code);
            Assert.Contains("At least one replay filter", result.Error?.Message);
        }

        [Fact]
        public async Task ProcessReplayJobs_Rejects_NonPositive_MaxJobs()
        {
            var service = BuildService();

            var result = await service.ProcessReplayJobsAsync(0, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("validation", result.Error?.Code);
        }

        private DeliveryRecoveryService BuildService()
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            return new DeliveryRecoveryService(
                new PostgresDeliveryRepository(factory),
                new PostgresAuditLogRepository(factory),
                new PostgresUnitOfWork(factory),
                new NullAppLogger<DeliveryRecoveryService>());
        }

        private async Task<Delivery> SeedDeadDeliveryAsync()
        {
            var (eventId, contractId, customerId) = await SeedAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var delivery = DeliveryFactory.CreatePending(
                eventId, contractId, customerId, "orders.created", "{}", destination, now);

            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var repo = new PostgresDeliveryRepository(factory);
            (await repo.InsertPendingAsync(new[] { delivery }, uow, CancellationToken.None)).EnsureSuccess();

            delivery.MarkInFlight(now.AddSeconds(1));
            delivery.MarkDead("final failure", now.AddSeconds(2));
            (await repo.UpdateAsync(delivery, now, uow, CancellationToken.None)).EnsureSuccess();

            var attempt = DeliveryAttempt.Create(
                delivery.Id,
                1,
                succeeded: false,
                failureCategory: "Permanent",
                error: "final failure",
                metadata: null,
                startedAt: now,
                completedAt: now.AddSeconds(2));
            (await repo.AddAttemptAsync(attempt, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();

            return delivery;
        }

        private async Task<Guid> SeedReplayDeliveryAsync(
            DeliveryStatus status,
            string eventType,
            DateTimeOffset createdAt)
        {
            var deliveryId = Guid.NewGuid();
            var (eventId, contractId, customerId) = await SeedAsync();

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                """
                INSERT INTO deliveries
                (id, event_id, contract_id, customer_id, event_type, payload, destination_type, destination_config, status, attempt_count, last_error, next_attempt_at, created_at, updated_at)
                VALUES (@id, @eventId, @contractId, @customerId, @eventType, '{}'::jsonb, 'Http', '{"url":"https://example.test"}'::jsonb, @status, 2, 'failed', NULL, @createdAt, @createdAt);
                """,
                new
                {
                    id = deliveryId,
                    eventId,
                    contractId,
                    customerId,
                    eventType,
                    status = status.ToString(),
                    createdAt,
                });

            return deliveryId;
        }

        private async Task<Delivery> SeedFailedDeliveryAsync()
        {
            var (eventId, contractId, customerId) = await SeedAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var delivery = DeliveryFactory.CreatePending(
                eventId, contractId, customerId, "orders.created", "{}", destination, now);

            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var repo = new PostgresDeliveryRepository(factory);
            (await repo.InsertPendingAsync(new[] { delivery }, uow, CancellationToken.None)).EnsureSuccess();

            delivery.MarkInFlight(now.AddSeconds(1));
            delivery.MarkFailed("temporary failure", now.AddMinutes(1), now.AddSeconds(2));
            (await repo.UpdateAsync(delivery, now, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();

            return delivery;
        }

        private async Task<IReadOnlyList<DeliveryAttempt>> ReadAttemptsAsync(Guid deliveryId)
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            var repo = new PostgresDeliveryRepository(factory);
            var attempts = await repo.GetAttemptsByDeliveryIdAsync(deliveryId, CancellationToken.None);
            return attempts.EnsureValue();
        }

        private async Task<long> ReadAuditCountAsync(Guid deliveryId)
        {
            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            return await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM delivery_operation_audits WHERE delivery_id = @id;",
                new { id = deliveryId });
        }

        private static ManualDeliveryOperationRequest NewRequest(string reason) =>
            new(Guid.NewGuid(), "operator-1", reason, "{\"source\":\"integration\"}");

        private async Task<(Guid eventId, Guid contractId, string customerId)> SeedAsync()
        {
            var customerId = "cust-" + Guid.NewGuid().ToString("N")[..6];
            var contractId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                "INSERT INTO customers(id, name, enabled, created_at, updated_at) VALUES (@id, @name, TRUE, @now, @now);",
                new { id = customerId, name = "Cust", now });

            await conn.ExecuteAsync("""
                INSERT INTO contracts(id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@id, @cid, TRUE, 'Http', '{"url":"https://example.test"}'::jsonb, @now, @now);
                """,
                new { id = contractId, cid = customerId, now });

            await conn.ExecuteAsync("""
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@id, 'orders.created', @idem, @src, @now, @now, '{}'::jsonb, '{}'::jsonb);
                """,
                new { id = eventId, idem = Guid.NewGuid().ToString("N"), src = Guid.NewGuid(), now });

            return (eventId, contractId, customerId);
        }

        private sealed class NullAppLogger<T> : IAppLogger<T>
        {
            public void Debug(string messageTemplate, params object?[] args) { }
            public void Information(string messageTemplate, params object?[] args) { }
            public void Warning(string messageTemplate, params object?[] args) { }
            public void Error(string messageTemplate, params object?[] args) { }
            public void Error(Exception exception, string messageTemplate, params object?[] args) { }
            public void Fatal(string messageTemplate, params object?[] args) { }
            public void Fatal(Exception exception, string messageTemplate, params object?[] args) { }
        }
    }
}

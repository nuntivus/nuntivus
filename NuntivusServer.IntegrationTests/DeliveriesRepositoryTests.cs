using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NuntivusServer.Application.Deliveries.Health;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Domain.Deliveries;
using NuntivusServer.Infastructure.Persistence.Postgres;
using Npgsql;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(DeliveriesCollection))]
    public sealed class DeliveriesRepositoryTests : IAsyncLifetime
    {
        private static readonly DeliveryFactory DeliveryFactory = new();

        private readonly DeliveriesFixture _fx;

        public DeliveriesRepositoryTests(DeliveriesFixture fx) => _fx = fx;

        public Task InitializeAsync() => _fx.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task InsertPendingAsync_Persists_All_Fields()
        {
            var (eventId, contractId, customerId) = await SeedAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var delivery = DeliveryFactory.CreatePending(
                eventId,
                contractId,
                customerId,
                "orders.created",
                "{\"a\":1}",
                destination,
                now,
                transformationConfiguration: "{\"Version\":1,\"FieldMappings\":[{\"SourceSelector\":\"order.id\",\"TargetSelector\":\"id\"}]}",
                rateLimit: ContractRateLimit.Create(10, null, 5),
                ordering: null,
                authenticationReference: "secret.acme.hmac",
                authenticationVersion: 7,
                transformationVersion: 1,
                schemaVersion: 4);

            var inserted = await InsertAsync(delivery);
            Assert.Equal(1, inserted);

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            var row = await conn.QuerySingleAsync<(
                Guid id, Guid event_id, Guid contract_id, string customer_id,
                string event_type, string payload, string destination_type, string destination_config,
                string? auth_ref, int? auth_version, int? transformation_version, int? schema_version,
                string status, int attempt_count, string? last_error, DateTimeOffset? next_attempt_at,
                DateTimeOffset created_at, DateTimeOffset updated_at)>(
                "SELECT id, event_id, contract_id, customer_id, event_type, payload::text, destination_type, destination_config::text, auth_ref, auth_version, transformation_version, schema_version, status, attempt_count, last_error, next_attempt_at, created_at, updated_at FROM deliveries;");

            Assert.Equal(delivery.Id, row.id);
            Assert.Equal(eventId, row.event_id);
            Assert.Equal(contractId, row.contract_id);
            Assert.Equal(customerId, row.customer_id);
            Assert.Equal("orders.created", row.event_type);
            Assert.Equal("Http", row.destination_type);
            Assert.Contains("example.test", row.destination_config);
            Assert.Equal("secret.acme.hmac", row.auth_ref);
            Assert.Equal(7, row.auth_version);
            Assert.Equal(1, row.transformation_version);
            Assert.Equal(4, row.schema_version);
            Assert.Equal("Pending", row.status);
            Assert.Equal(0, row.attempt_count);
            Assert.Null(row.last_error);
            Assert.Null(row.next_attempt_at);
        }

        [Fact]
        public async Task InsertPendingAsync_rejects_missing_snapshot_secret()
        {
            var (eventId, contractId, customerId) = await SeedAsync();
            var delivery = DeliveryFactory.CreatePending(
                eventId, contractId, customerId, "orders.created", "{}",
                new Destination(DestinationType.Nats,
                    "{\"Credentials\":{\"Token\":{\"Kind\":\"ref\",\"Value\":\"secret.acme.missing\"}}}"),
                DateTimeOffset.UtcNow);
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);

            var result = await new PostgresDeliveryRepository(factory).InsertPendingAsync(
                new[] { delivery }, uow, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains("secret.acme.missing", result.Errors[0].Message);
            uow.Rollback();
            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM deliveries"));
        }

        [Fact]
        public async Task InsertPendingAsync_Deduplicates_On_EventId_And_ContractId()
        {
            var (eventId, contractId, customerId) = await SeedAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var first = DeliveryFactory.CreatePending(eventId, contractId, customerId, "orders.created", "{}", destination, now);
            var duplicate = DeliveryFactory.CreatePending(eventId, contractId, customerId, "orders.created", "{}", destination, now.AddSeconds(1));

            var inserted1 = await InsertAsync(first);
            var inserted2 = await InsertAsync(duplicate);

            Assert.Equal(1, inserted1);
            Assert.Equal(0, inserted2);

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM deliveries;");
            Assert.Equal(1L, count);
        }

        [Fact]
        public async Task Ids_Are_UuidV7_And_Time_Ordered()
        {
            var (eventId1, contractId, customerId) = await SeedAsync();
            var eventId2 = await SeedExtraEventAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var t0 = DateTimeOffset.UtcNow;

            var a = DeliveryFactory.CreatePending(eventId1, contractId, customerId, "orders.created", "{}", destination, t0);
            var b = DeliveryFactory.CreatePending(eventId2, contractId, customerId, "orders.created", "{}", destination, t0.AddSeconds(1));

            await InsertAsync(a);
            await InsertAsync(b);

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            var ordered = (await conn.QueryAsync<Guid>("SELECT id FROM deliveries ORDER BY id;")).ToList();
            Assert.Equal(new[] { a.Id, b.Id }, ordered);
        }

        [Fact]
        public async Task AddAttemptAsync_And_GetAttemptsByDeliveryIdAsync_Persist_And_Query_Attempts()
        {
            var (eventId1, contractId, customerId) = await SeedAsync();
            var eventId2 = await SeedExtraEventAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var deliveryA = DeliveryFactory.CreatePending(eventId1, contractId, customerId, "orders.created", "{}", destination, now);
            var deliveryB = DeliveryFactory.CreatePending(eventId2, contractId, customerId, "orders.created", "{}", destination, now.AddSeconds(1));
            await InsertAsync(deliveryA);
            await InsertAsync(deliveryB);

            var a1 = DeliveryAttempt.Create(
                deliveryA.Id,
                1,
                succeeded: false,
                failureCategory: "Transient",
                error: "timeout",
                metadata: new Dictionary<string, string> { ["httpStatus"] = "504" },
                startedAt: now,
                completedAt: now.AddSeconds(1));
            var a2 = DeliveryAttempt.Create(
                deliveryA.Id,
                2,
                succeeded: true,
                failureCategory: null,
                error: null,
                metadata: new Dictionary<string, string> { ["httpStatus"] = "202" },
                startedAt: now.AddSeconds(2),
                completedAt: now.AddSeconds(3));
            var b1 = DeliveryAttempt.Create(
                deliveryB.Id,
                1,
                succeeded: true,
                failureCategory: null,
                error: null,
                metadata: new Dictionary<string, string> { ["httpStatus"] = "200" },
                startedAt: now.AddSeconds(1),
                completedAt: now.AddSeconds(2));

            await AddAttemptAsync(a1);
            await AddAttemptAsync(a2);
            await AddAttemptAsync(b1);

            var attemptsA = await GetAttemptsAsync(deliveryA.Id);

            Assert.Equal(2, attemptsA.Count);
            Assert.Equal(1, attemptsA[0].AttemptNumber);
            Assert.Equal(2, attemptsA[1].AttemptNumber);
            Assert.False(attemptsA[0].Succeeded);
            Assert.True(attemptsA[1].Succeeded);
            Assert.Equal("504", attemptsA[0].Metadata["httpStatus"]);
            Assert.Equal("202", attemptsA[1].Metadata["httpStatus"]);
        }

        [Fact]
        public async Task AddAttemptAsync_Deduplicates_On_Delivery_And_AttemptNumber()
        {
            var (eventId, contractId, customerId) = await SeedAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var delivery = DeliveryFactory.CreatePending(eventId, contractId, customerId, "orders.created", "{}", destination, now);
            await InsertAsync(delivery);

            var first = DeliveryAttempt.Create(
                delivery.Id,
                1,
                succeeded: false,
                failureCategory: "Transient",
                error: "timeout",
                metadata: new Dictionary<string, string> { ["httpStatus"] = "504" },
                startedAt: now,
                completedAt: now.AddSeconds(1));
            var duplicate = DeliveryAttempt.Create(
                delivery.Id,
                1,
                succeeded: true,
                failureCategory: null,
                error: null,
                metadata: new Dictionary<string, string> { ["httpStatus"] = "200" },
                startedAt: now.AddSeconds(2),
                completedAt: now.AddSeconds(3));

            var inserted1 = await AddAttemptAsync(first);
            var inserted2 = await AddAttemptAsync(duplicate);
            var attempts = await GetAttemptsAsync(delivery.Id);

            Assert.Equal(1, inserted1);
            Assert.Equal(0, inserted2);
            Assert.Single(attempts);
            Assert.Equal("504", attempts[0].Metadata["httpStatus"]);
        }

        [Fact]
        public async Task ListDeadAsync_Returns_Only_Dead_Deliveries()
        {
            var (eventId1, contractId, customerId) = await SeedAsync();
            var eventId2 = await SeedExtraEventAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var dead = DeliveryFactory.CreatePending(eventId1, contractId, customerId, "orders.created", "{}", destination, now);
            var alive = DeliveryFactory.CreatePending(eventId2, contractId, customerId, "orders.created", "{}", destination, now.AddSeconds(1));
            await InsertAsync(dead);
            await InsertAsync(alive);

            dead.MarkInFlight(now.AddSeconds(2));
            dead.MarkDead("final failure", now.AddSeconds(3));
            await UpdateAsync(dead, now);

            alive.MarkInFlight(now.AddSeconds(2));
            alive.MarkFailed("retry", now.AddMinutes(1), now.AddSeconds(3));
            await UpdateAsync(alive, now.AddSeconds(1));

            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            var repo = new PostgresDeliveryRepository(factory);
            var page = await repo.ListDeadAsync(1, 20, CancellationToken.None);
            var deadRows = page.EnsureValue();

            Assert.Single(deadRows.Items);
            Assert.Equal(dead.Id, deadRows.Items[0].Id);
            Assert.Equal(DeliveryStatus.Dead, deadRows.Items[0].Status);
        }

        [Fact]
        public async Task ClaimDueForSchedulingAsync_Claims_Due_Deliveries_And_Normalizes_State()
        {
            var (eventId, contractId, customerId) = await SeedAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var duePending = DeliveryFactory.CreatePending(eventId, contractId, customerId, "orders.created", "{}", destination, now);
            var dueFailed = DeliveryFactory.CreatePending(await SeedExtraEventAsync(), contractId, customerId, "orders.created", "{}", destination, now.AddSeconds(1));
            var futurePending = DeliveryFactory.CreatePending(await SeedExtraEventAsync(), contractId, customerId, "orders.created", "{}", destination, now.AddSeconds(2));

            await InsertAsync(duePending);
            await InsertAsync(dueFailed);
            await InsertAsync(futurePending);

            var expectedDueFailedUpdatedAt = dueFailed.UpdatedAt;
            dueFailed.MarkInFlight(now.AddSeconds(3));
            dueFailed.MarkFailed("retry", now.AddSeconds(-2), now.AddSeconds(4));
            await UpdateAsync(dueFailed, expectedDueFailedUpdatedAt);

            Assert.True(await ScheduleNextAttemptAsync(duePending.Id, duePending.UpdatedAt, now.AddSeconds(-1)));
            Assert.True(await ScheduleNextAttemptAsync(futurePending.Id, futurePending.UpdatedAt, now.AddMinutes(2)));

            var claimed = await ClaimDueAsync(now, batchSize: 10);

            Assert.Equal(2, claimed.Count);
            Assert.Contains(claimed, d => d.Id == duePending.Id);
            Assert.Contains(claimed, d => d.Id == dueFailed.Id);
            Assert.All(claimed, d =>
            {
                Assert.Equal(DeliveryStatus.Pending, d.Status);
                Assert.Null(d.NextAttemptAt);
            });
        }

        [Fact]
        public async Task ClaimDueForSchedulingAsync_Does_Not_Claim_Already_Claimed_Deliveries()
        {
            var (eventId, contractId, customerId) = await SeedAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var due = DeliveryFactory.CreatePending(eventId, contractId, customerId, "orders.created", "{}", destination, now);
            await InsertAsync(due);
            Assert.True(await ScheduleNextAttemptAsync(due.Id, due.UpdatedAt, now.AddSeconds(-1)));

            var first = await ClaimDueAsync(now, batchSize: 10);
            var second = await ClaimDueAsync(now.AddSeconds(1), batchSize: 10);

            Assert.Single(first);
            Assert.Equal(due.Id, first[0].Id);
            Assert.Empty(second);
        }

        [Fact]
        public async Task UpsertDestinationHealthAsync_Tracks_Failures_Success_And_Rolling_Latency()
        {
            var (_, contractId, customerId) = await SeedAsync();
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            var repo = new PostgresDeliveryRepository(factory);

            var t1 = DateTimeOffset.UtcNow;
            await UpsertDestinationHealthAsync(
                repo,
                factory,
                new DestinationHealthObservation(contractId, customerId, "Http", false, t1, 100));

            var t2 = t1.AddSeconds(1);
            await UpsertDestinationHealthAsync(
                repo,
                factory,
                new DestinationHealthObservation(contractId, customerId, "Http", false, t2, 200));

            var t3 = t2.AddSeconds(1);
            await UpsertDestinationHealthAsync(
                repo,
                factory,
                new DestinationHealthObservation(contractId, customerId, "Http", false, t3, null));

            var t4 = t3.AddSeconds(1);
            await UpsertDestinationHealthAsync(
                repo,
                factory,
                new DestinationHealthObservation(contractId, customerId, "Http", true, t4, 300));

            var byContract = (await repo.GetDestinationHealthByContractIdAsync(contractId, CancellationToken.None)).EnsureValue();
            Assert.NotNull(byContract);
            Assert.Equal(contractId, byContract!.ContractId);
            Assert.Equal(customerId, byContract.CustomerId);
            Assert.Equal(DestinationHealthStatus.Healthy, byContract.Status);
            Assert.Equal(0, byContract.ConsecutiveFailures);
            Assert.True(byContract.LastSuccessAt.HasValue);
            Assert.True(byContract.LastFailureAt.HasValue);
            Assert.True((byContract.LastSuccessAt!.Value - t4).Duration() < TimeSpan.FromMilliseconds(1));
            Assert.True((byContract.LastFailureAt!.Value - t3).Duration() < TimeSpan.FromMilliseconds(1));
            Assert.True((byContract.LastAttemptAt - t4).Duration() < TimeSpan.FromMilliseconds(1));
            Assert.Equal(300d, byContract.LastLatencyMs);
            Assert.Equal(200d, byContract.AverageLatencyMs);
            Assert.Equal(3, byContract.LatencySampleCount);

            var byCustomer = (await repo.ListDestinationHealthByCustomerIdAsync(customerId, CancellationToken.None)).EnsureValue();
            Assert.Single(byCustomer);
            Assert.Equal(contractId, byCustomer[0].ContractId);
        }

        private async Task<int> InsertAsync(Delivery delivery)
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var repo = new PostgresDeliveryRepository(factory);
            var result = await repo.InsertPendingAsync(new[] { delivery }, uow, CancellationToken.None);
            uow.Commit();
            return result.EnsureValue();
        }

        private static async Task UpsertDestinationHealthAsync(
            PostgresDeliveryRepository repo,
            PostgresDatabaseConnectionFactory factory,
            DestinationHealthObservation observation)
        {
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            (await repo.UpsertDestinationHealthAsync(observation, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();
        }

        private async Task<bool> ScheduleNextAttemptAsync(Guid deliveryId, DateTimeOffset expectedUpdatedAt, DateTimeOffset nextAttemptAt)
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var repo = new PostgresDeliveryRepository(factory);
            var result = await repo.ScheduleNextAttemptAsync(deliveryId, expectedUpdatedAt, nextAttemptAt, uow, CancellationToken.None);
            uow.Commit();
            return result.EnsureValue();
        }

        private async Task<IReadOnlyList<Delivery>> ClaimDueAsync(DateTimeOffset now, int batchSize)
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var repo = new PostgresDeliveryRepository(factory);
            var result = await repo.ClaimDueForSchedulingAsync(now, batchSize, uow, CancellationToken.None);
            uow.Commit();
            return result.EnsureValue();
        }

        private async Task<int> AddAttemptAsync(DeliveryAttempt attempt)
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var repo = new PostgresDeliveryRepository(factory);
            var result = await repo.AddAttemptAsync(attempt, uow, CancellationToken.None);
            uow.Commit();
            return result.EnsureValue();
        }

        private async Task<IReadOnlyList<DeliveryAttempt>> GetAttemptsAsync(Guid deliveryId)
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            var repo = new PostgresDeliveryRepository(factory);
            var result = await repo.GetAttemptsByDeliveryIdAsync(deliveryId, CancellationToken.None);
            return result.EnsureValue();
        }

        private async Task<bool> UpdateAsync(Delivery delivery, DateTimeOffset expectedUpdatedAt)
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var repo = new PostgresDeliveryRepository(factory);
            var result = await repo.UpdateAsync(delivery, expectedUpdatedAt, uow, CancellationToken.None);
            uow.Commit();
            return result.EnsureValue();
        }

        private async Task<(Guid eventId, Guid contractId, string customerId)> SeedAsync()
        {
            var customerId = "cust-1";
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

        private async Task<Guid> SeedExtraEventAsync()
        {
            var eventId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync("""
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@id, 'orders.created', @idem, @src, @now, @now, '{}'::jsonb, '{}'::jsonb);
                """,
                new { id = eventId, idem = Guid.NewGuid().ToString("N"), src = Guid.NewGuid(), now });
            return eventId;
        }
    }
}

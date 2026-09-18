using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Deliveries.Recovery;
using KairoServer.Application.Persistence;
using KairoServer.Common.Logging;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Deliveries;
using KairoServer.Infastructure.Persistence.Postgres;
using Npgsql;

namespace KairoServer.IntegrationTests
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

            var first = await service.ManualRetryAsync(delivery.Id, CancellationToken.None);
            var second = await service.ManualRetryAsync(delivery.Id, CancellationToken.None);

            first.EnsureSuccess();
            second.EnsureSuccess();

            Assert.Equal(DeliveryStatus.Pending, first.Value.Status);
            Assert.Equal(0, first.Value.AttemptCount);
            Assert.Equal(DeliveryStatus.Pending, second.Value.Status);
            Assert.Equal(0, second.Value.AttemptCount);

            var attempts = await ReadAttemptsAsync(delivery.Id);
            Assert.Single(attempts);
        }

        [Fact]
        public async Task ManualRequeueAsync_Preserves_AttemptCount_And_Is_Idempotent()
        {
            var delivery = await SeedDeadDeliveryAsync();
            var service = BuildService();

            var first = await service.ManualRequeueAsync(delivery.Id, CancellationToken.None);
            var second = await service.ManualRequeueAsync(delivery.Id, CancellationToken.None);

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

        private DeliveryRecoveryService BuildService()
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            return new DeliveryRecoveryService(
                new PostgresDeliveryRepository(factory),
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

        private async Task<IReadOnlyList<DeliveryAttempt>> ReadAttemptsAsync(Guid deliveryId)
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            var repo = new PostgresDeliveryRepository(factory);
            var attempts = await repo.GetAttemptsByDeliveryIdAsync(deliveryId, CancellationToken.None);
            return attempts.EnsureValue();
        }

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

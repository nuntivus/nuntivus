using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Deliveries;
using KairoServer.Infastructure.Persistence.Postgres;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(DeliveriesCollection))]
    public sealed class DeliveriesRepositoryTests : IAsyncLifetime
    {
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

            var delivery = Delivery.CreatePending(
                eventId, contractId, customerId, "orders.created", "{\"a\":1}", destination, now);

            var inserted = await InsertAsync(delivery);
            Assert.Equal(1, inserted);

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            var row = await conn.QuerySingleAsync<(
                Guid id, Guid event_id, Guid contract_id, string customer_id,
                string event_type, string payload, string destination_type, string destination_config,
                string status, int attempt_count, string? last_error, DateTimeOffset? next_attempt_at,
                DateTimeOffset created_at, DateTimeOffset updated_at)>(
                "SELECT id, event_id, contract_id, customer_id, event_type, payload::text, destination_type, destination_config::text, status, attempt_count, last_error, next_attempt_at, created_at, updated_at FROM deliveries;");

            Assert.Equal(delivery.Id, row.id);
            Assert.Equal(eventId, row.event_id);
            Assert.Equal(contractId, row.contract_id);
            Assert.Equal(customerId, row.customer_id);
            Assert.Equal("orders.created", row.event_type);
            Assert.Equal("Http", row.destination_type);
            Assert.Contains("example.test", row.destination_config);
            Assert.Equal("Pending", row.status);
            Assert.Equal(0, row.attempt_count);
            Assert.Null(row.last_error);
            Assert.Null(row.next_attempt_at);
        }

        [Fact]
        public async Task InsertPendingAsync_Deduplicates_On_EventId_And_ContractId()
        {
            var (eventId, contractId, customerId) = await SeedAsync();
            var destination = new Destination(DestinationType.Http, "{\"url\":\"https://example.test\"}");
            var now = DateTimeOffset.UtcNow;

            var first = Delivery.CreatePending(eventId, contractId, customerId, "orders.created", "{}", destination, now);
            var duplicate = Delivery.CreatePending(eventId, contractId, customerId, "orders.created", "{}", destination, now.AddSeconds(1));

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

            var a = Delivery.CreatePending(eventId1, contractId, customerId, "orders.created", "{}", destination, t0);
            var b = Delivery.CreatePending(eventId2, contractId, customerId, "orders.created", "{}", destination, t0.AddSeconds(1));

            await InsertAsync(a);
            await InsertAsync(b);

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            var ordered = (await conn.QueryAsync<Guid>("SELECT id FROM deliveries ORDER BY id;")).ToList();
            Assert.Equal(new[] { a.Id, b.Id }, ordered);
        }

        private async Task<int> InsertAsync(Delivery delivery)
        {
            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var repo = new PostgresDeliveryRepository();
            var result = await repo.InsertPendingAsync(new[] { delivery }, uow, CancellationToken.None);
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

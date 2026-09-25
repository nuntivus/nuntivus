using System.Net;
using System.Net.Http.Json;
using Dapper;
using KairoServer.Contracts.Dashboard;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(DeliveriesApiCollection))]
    public sealed class AdminOverviewApiTests : IAsyncLifetime
    {
        private readonly DeliveriesApiFixture _fixture;
        private readonly HttpClient _client;

        public AdminOverviewApiTests(DeliveriesApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Get_overview_returns_aggregated_counts()
        {
            var now = DateTimeOffset.UtcNow;
            var customerA = "cust-a";
            var customerB = "cust-b";
            var contractId = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO customers(id, name, enabled, created_at, updated_at)
                VALUES
                    (@customerA, 'Customer A', TRUE, @now, @now),
                    (@customerB, 'Customer B', TRUE, @now, @now);
                """,
                new { customerA, customerB, now });

            await conn.ExecuteAsync(
                """
                INSERT INTO contracts(id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@contractId, @customerA, TRUE, 'Http', '{"url":"https://example.test"}'::jsonb, @now, @now);
                """,
                new { contractId, customerA, now });

            await InsertDeliveryAsync(conn, Guid.NewGuid(), contractId, customerA, "Pending", now);
            await InsertDeliveryAsync(conn, Guid.NewGuid(), contractId, customerA, "Failed", now.AddSeconds(1));
            await InsertDeliveryAsync(conn, Guid.NewGuid(), contractId, customerA, "Dead", now.AddSeconds(2));
            await InsertDeliveryAsync(conn, Guid.NewGuid(), contractId, customerA, "Delivered", now.AddSeconds(3));

            var response = await _client.GetAsync("/v1/admin/overview");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<DashboardOverviewView>();
            Assert.NotNull(body);
            Assert.Equal(2L, body!.TotalCustomers);
            Assert.Equal(1L, body.TotalPendingDeliveries);
            Assert.Equal(1L, body.TotalFailedDeliveries);
            Assert.Equal(1L, body.TotalDeadDeliveries);
        }

        [Fact]
        public async Task Get_timeseries_returns_buckets_and_status_counts_within_window()
        {
            var now = DateTimeOffset.UtcNow;
            var contractId = Guid.NewGuid();
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                "INSERT INTO customers(id, name, enabled, created_at, updated_at) VALUES ('chart-customer', 'Chart Customer', TRUE, @now, @now);",
                new { now });
            await conn.ExecuteAsync(
                "INSERT INTO contracts(id, customer_id, enabled, destination_type, destination_config, created_at, updated_at) VALUES (@contractId, 'chart-customer', TRUE, 'Http', '{\"url\":\"https://example.test\"}'::jsonb, @now, @now);",
                new { contractId, now });

            await InsertDeliveryAsync(conn, Guid.NewGuid(), contractId, "chart-customer", "Delivered", now.AddSeconds(-5));
            await InsertDeliveryAsync(conn, Guid.NewGuid(), contractId, "chart-customer", "Failed", now.AddSeconds(-5));
            await InsertDeliveryAsync(conn, Guid.NewGuid(), contractId, "chart-customer", "Dead", now.AddSeconds(-5));
            await InsertDeliveryAsync(conn, Guid.NewGuid(), contractId, "chart-customer", "Delivered", now.AddSeconds(-90));

            var response = await _client.GetAsync("/v1/admin/overview/timeseries?rangeSeconds=15");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<DashboardTimeSeriesView>();
            Assert.NotNull(body);
            Assert.Equal(15, body!.RangeSeconds);
            Assert.Equal(15, body.Buckets.Count);
            Assert.Equal(3, body.Buckets.Sum(bucket => bucket.Events));
            Assert.Equal(3, body.Buckets.Sum(bucket => bucket.Deliveries));
            Assert.Equal(1, body.Buckets.Sum(bucket => bucket.Delivered));
            Assert.Equal(1, body.Buckets.Sum(bucket => bucket.Failed));
            Assert.Equal(1, body.Buckets.Sum(bucket => bucket.Dead));
            Assert.Contains(body.Buckets, bucket => bucket.Events == 0 && bucket.Deliveries == 0);
        }

        [Fact]
        public async Task Get_timeseries_rejects_unsupported_window()
        {
            var response = await _client.GetAsync("/v1/admin/overview/timeseries?rangeSeconds=86400");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        private static async Task InsertDeliveryAsync(
            NpgsqlConnection conn,
            Guid eventId,
            Guid contractId,
            string customerId,
            string status,
            DateTimeOffset now)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@eventId, 'orders.created', @idempotencyKey, @sourceId, @now, @now, '{}'::jsonb, '{"id":"1"}'::jsonb);
                """,
                new { eventId, idempotencyKey = $"idem-{eventId:N}", sourceId = Guid.NewGuid(), now });

            await conn.ExecuteAsync(
                """
                INSERT INTO deliveries
                (
                    id,
                    event_id,
                    contract_id,
                    customer_id,
                    event_type,
                    payload,
                    destination_type,
                    destination_config,
                    status,
                    attempt_count,
                    created_at,
                    updated_at
                )
                VALUES
                (
                    @deliveryId,
                    @eventId,
                    @contractId,
                    @customerId,
                    'orders.created',
                    '{"id":"1"}'::jsonb,
                    'Http',
                    '{"url":"https://example.test"}'::jsonb,
                    @status,
                    0,
                    @now,
                    @now
                );
                """,
                new
                {
                    deliveryId = Guid.NewGuid(),
                    eventId,
                    contractId,
                    customerId,
                    status,
                    now,
                });
        }
    }
}

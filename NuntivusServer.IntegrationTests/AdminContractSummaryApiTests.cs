using System.Net;
using System.Net.Http.Json;
using Dapper;
using NuntivusServer.Api.Common;
using NuntivusServer.Contracts.Dashboard;
using NuntivusServer.Contracts.CustomerContracts;
using Npgsql;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(DeliveriesApiCollection))]
    public sealed class AdminContractSummaryApiTests : IAsyncLifetime
    {
        private readonly DeliveriesApiFixture _fixture;
        private readonly HttpClient _client;

        public AdminContractSummaryApiTests(DeliveriesApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Get_summary_returns_contract_delivery_and_health_aggregates()
        {
            var now = DateTimeOffset.UtcNow;
            var customerId = "cust-contract-summary";
            var contractId = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO customers(id, name, enabled, created_at, updated_at)
                VALUES (@customerId, 'Contract Customer', TRUE, @now, @now);
                """,
                new { customerId, now });

            await conn.ExecuteAsync(
                """
                INSERT INTO contracts(id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@contractId, @customerId, TRUE, 'Http', '{"url":"https://example.test"}'::jsonb, @now, @now);
                """,
                new { contractId, customerId, now });

            await conn.ExecuteAsync(
                "INSERT INTO contract_event_types(contract_id, event_type) VALUES (@contractId, 'orders.created');",
                new { contractId });

            await InsertDeliveryAsync(conn, contractId, customerId, "Pending", now);
            await InsertDeliveryAsync(conn, contractId, customerId, "InFlight", now.AddSeconds(1));
            await InsertDeliveryAsync(conn, contractId, customerId, "Delivered", now.AddSeconds(2));
            await InsertDeliveryAsync(conn, contractId, customerId, "Failed", now.AddSeconds(3));
            await InsertDeliveryAsync(conn, contractId, customerId, "Dead", now.AddSeconds(4));

            await conn.ExecuteAsync(
                """
                INSERT INTO destination_health
                (
                    contract_id,
                    customer_id,
                    destination_type,
                    consecutive_failures,
                    last_success_at,
                    last_failure_at,
                    last_attempt_at,
                    last_latency_ms,
                    avg_latency_ms,
                    latency_sample_count,
                    health_status,
                    created_at,
                    updated_at
                )
                VALUES
                (
                    @contractId,
                    @customerId,
                    'Http',
                    2,
                    @lastSuccessAt,
                    @lastFailureAt,
                    @now,
                    120,
                    90,
                    10,
                    'Degraded',
                    @now,
                    @now
                );
                """,
                new
                {
                    contractId,
                    customerId,
                    now,
                    lastSuccessAt = now.AddMinutes(-5),
                    lastFailureAt = now.AddMinutes(-1),
                });

            var response = await _client.GetAsync($"/v1/admin/contracts/{contractId:D}/summary");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ContractAdminSummaryView>();
            Assert.NotNull(body);
            Assert.Equal(contractId, body!.ContractId);
            Assert.Equal(customerId, body.CustomerId);
            Assert.True(body.ContractEnabled);
            Assert.Equal(DestinationType.Http, body.DestinationType);
            Assert.Equal(1, body.EventTypeCount);
            Assert.Equal(1L, body.PendingDeliveries);
            Assert.Equal(1L, body.InFlightDeliveries);
            Assert.Equal(1L, body.DeliveredDeliveries);
            Assert.Equal(1L, body.FailedDeliveries);
            Assert.Equal(1L, body.DeadDeliveries);
            Assert.NotNull(body.DestinationHealth);
            Assert.Equal("Degraded", body.DestinationHealth!.Status);
            Assert.Equal(2, body.DestinationHealth.ConsecutiveFailures);
        }

        [Fact]
        public async Task Get_summary_returns_404_when_contract_missing()
        {
            var response = await _client.GetAsync($"/v1/admin/contracts/{Guid.NewGuid():D}/summary");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.NotNull(error);
            Assert.Equal("not_found", error!.Code);
        }

        private static async Task InsertDeliveryAsync(
            NpgsqlConnection conn,
            Guid contractId,
            string customerId,
            string status,
            DateTimeOffset now)
        {
            var eventId = Guid.NewGuid();

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

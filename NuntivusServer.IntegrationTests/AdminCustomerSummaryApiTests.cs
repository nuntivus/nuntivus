using System.Net;
using System.Net.Http.Json;
using Dapper;
using NuntivusServer.Api.Common;
using NuntivusServer.Contracts.Dashboard;
using Npgsql;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(DeliveriesApiCollection))]
    public sealed class AdminCustomerSummaryApiTests : IAsyncLifetime
    {
        private readonly DeliveriesApiFixture _fixture;
        private readonly HttpClient _client;

        public AdminCustomerSummaryApiTests(DeliveriesApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Get_summary_returns_customer_contract_and_health_aggregates()
        {
            var now = DateTimeOffset.UtcNow;
            var customerId = "cust-summary";
            var contract1 = Guid.NewGuid();
            var contract2 = Guid.NewGuid();
            var contract3 = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO customers(id, name, enabled, created_at, updated_at)
                VALUES (@customerId, 'Summary Customer', TRUE, @now, @now);
                """,
                new { customerId, now });

            await InsertContractAsync(conn, contract1, customerId, enabled: true, now);
            await InsertContractAsync(conn, contract2, customerId, enabled: true, now.AddSeconds(1));
            await InsertContractAsync(conn, contract3, customerId, enabled: false, now.AddSeconds(2));

            await InsertDestinationHealthAsync(conn, contract1, customerId, "Healthy", now);
            await InsertDestinationHealthAsync(conn, contract2, customerId, "Degraded", now);
            await InsertDestinationHealthAsync(conn, contract3, customerId, "Unhealthy", now);

            var response = await _client.GetAsync($"/v1/admin/customers/{customerId}/summary");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CustomerAdminSummaryView>();
            Assert.NotNull(body);
            Assert.Equal(customerId, body!.CustomerId);
            Assert.Equal("Summary Customer", body.CustomerName);
            Assert.True(body.CustomerEnabled);
            Assert.Equal(3L, body.TotalContracts);
            Assert.Equal(2L, body.EnabledContracts);
            Assert.Equal(1L, body.DisabledContracts);
            Assert.Equal(1, body.HealthyDestinations);
            Assert.Equal(1, body.DegradedDestinations);
            Assert.Equal(1, body.UnhealthyDestinations);
        }

        [Fact]
        public async Task Get_summary_returns_404_when_customer_missing()
        {
            var response = await _client.GetAsync("/v1/admin/customers/missing-customer/summary");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.NotNull(error);
            Assert.Equal("not_found", error!.Code);
        }

        private static async Task InsertContractAsync(
            NpgsqlConnection conn,
            Guid contractId,
            string customerId,
            bool enabled,
            DateTimeOffset now)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO contracts(id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@contractId, @customerId, @enabled, 'Http', '{"url":"https://example.test"}'::jsonb, @now, @now);
                """,
                new { contractId, customerId, enabled, now });

            await conn.ExecuteAsync(
                "INSERT INTO contract_event_types(contract_id, event_type) VALUES (@contractId, 'orders.created');",
                new { contractId });
        }

        private static async Task InsertDestinationHealthAsync(
            NpgsqlConnection conn,
            Guid contractId,
            string customerId,
            string status,
            DateTimeOffset now)
        {
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
                    0,
                    @now,
                    NULL,
                    @now,
                    20,
                    25,
                    2,
                    @status,
                    @now,
                    @now
                );
                """,
                new { contractId, customerId, status, now });
        }
    }
}

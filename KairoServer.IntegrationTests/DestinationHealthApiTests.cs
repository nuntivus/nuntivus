using System.Net;
using System.Net.Http.Json;
using Dapper;
using KairoServer.Application.Deliveries.Health;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(DeliveriesApiCollection))]
    public sealed class DestinationHealthApiTests : IAsyncLifetime
    {
        private readonly DeliveriesApiFixture _fixture;
        private readonly HttpClient _client;

        public DestinationHealthApiTests(DeliveriesApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Get_contract_health_endpoint_returns_snapshot()
        {
            var now = DateTimeOffset.UtcNow;
            var (customerId, contractId) = await SeedContractAsync(now);

            await InsertDestinationHealthAsync(
                contractId,
                customerId,
                "Http",
                consecutiveFailures: 2,
                lastSuccessAt: now.AddMinutes(-5),
                lastFailureAt: now.AddMinutes(-1),
                lastAttemptAt: now,
                lastLatencyMs: 240,
                averageLatencyMs: 180,
                latencySampleCount: 5,
                status: "Degraded",
                updatedAt: now);

            var response = await _client.GetAsync($"/v1/destination-health/contracts/{contractId:D}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<DestinationHealthDto>();
            Assert.NotNull(body);
            Assert.Equal(contractId, body!.ContractId);
            Assert.Equal(customerId, body.CustomerId);
            Assert.Equal(2, body.ConsecutiveFailures);
            Assert.Equal(DestinationHealthStatus.Degraded, body.Status);
            Assert.Equal(240d, body.LastLatencyMs);
            Assert.Equal(180d, body.AverageLatencyMs);
        }

        [Fact]
        public async Task List_customer_health_endpoint_returns_contract_rows_ordered_by_severity()
        {
            var now = DateTimeOffset.UtcNow;
            var (customerId, unhealthyContract) = await SeedContractAsync(now);
            var (_, healthyContract) = await SeedContractAsync(now.AddSeconds(1), customerId);

            await InsertDestinationHealthAsync(
                unhealthyContract,
                customerId,
                "Http",
                consecutiveFailures: 3,
                lastSuccessAt: null,
                lastFailureAt: now,
                lastAttemptAt: now,
                lastLatencyMs: 500,
                averageLatencyMs: 420,
                latencySampleCount: 4,
                status: "Unhealthy",
                updatedAt: now);

            await InsertDestinationHealthAsync(
                healthyContract,
                customerId,
                "Kafka",
                consecutiveFailures: 0,
                lastSuccessAt: now,
                lastFailureAt: null,
                lastAttemptAt: now,
                lastLatencyMs: 30,
                averageLatencyMs: 40,
                latencySampleCount: 10,
                status: "Healthy",
                updatedAt: now.AddSeconds(1));

            var response = await _client.GetAsync($"/v1/destination-health/customers/{customerId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rows = await response.Content.ReadFromJsonAsync<List<DestinationHealthDto>>();
            Assert.NotNull(rows);
            Assert.Equal(2, rows!.Count);
            Assert.Equal(unhealthyContract, rows[0].ContractId);
            Assert.Equal(DestinationHealthStatus.Unhealthy, rows[0].Status);
            Assert.Equal(healthyContract, rows[1].ContractId);
            Assert.Equal(DestinationHealthStatus.Healthy, rows[1].Status);
        }

        private async Task<(string customerId, Guid contractId)> SeedContractAsync(DateTimeOffset now, string? existingCustomerId = null)
        {
            var customerId = existingCustomerId ?? ("cust-" + Guid.NewGuid().ToString("N")[..8]);
            var contractId = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            if (existingCustomerId is null)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO customers(id, name, enabled, created_at, updated_at) VALUES (@id, @name, TRUE, @now, @now);",
                    new { id = customerId, name = "Cust", now });
            }

            await conn.ExecuteAsync(
                """
                INSERT INTO contracts(id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@id, @customerId, TRUE, 'Http', '{"url":"https://example.test"}'::jsonb, @now, @now);
                """,
                new { id = contractId, customerId, now });

            return (customerId, contractId);
        }

        private async Task InsertDestinationHealthAsync(
            Guid contractId,
            string customerId,
            string destinationType,
            int consecutiveFailures,
            DateTimeOffset? lastSuccessAt,
            DateTimeOffset? lastFailureAt,
            DateTimeOffset lastAttemptAt,
            double? lastLatencyMs,
            double? averageLatencyMs,
            long latencySampleCount,
            string status,
            DateTimeOffset updatedAt)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

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
                    @destinationType,
                    @consecutiveFailures,
                    @lastSuccessAt,
                    @lastFailureAt,
                    @lastAttemptAt,
                    @lastLatencyMs,
                    @averageLatencyMs,
                    @latencySampleCount,
                    @status,
                    @updatedAt,
                    @updatedAt
                );
                """,
                new
                {
                    contractId,
                    customerId,
                    destinationType,
                    consecutiveFailures,
                    lastSuccessAt,
                    lastFailureAt,
                    lastAttemptAt,
                    lastLatencyMs,
                    averageLatencyMs,
                    latencySampleCount,
                    status,
                    updatedAt,
                });
        }

        private sealed record DestinationHealthDto(
            Guid ContractId,
            string CustomerId,
            string DestinationType,
            int ConsecutiveFailures,
            DateTimeOffset? LastSuccessAt,
            DateTimeOffset? LastFailureAt,
            DateTimeOffset LastAttemptAt,
            double? LastLatencyMs,
            double? AverageLatencyMs,
            long LatencySampleCount,
            DestinationHealthStatus Status,
            DateTimeOffset UpdatedAt);
    }
}

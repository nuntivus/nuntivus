using System.Net;
using System.Net.Http.Json;
using Dapper;
using KairoServer.Api.Common;
using KairoServer.Contracts.Dashboard;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(DeliveriesApiCollection))]
    public sealed class AdminDeliveryFailuresApiTests : IAsyncLifetime
    {
        private readonly DeliveriesApiFixture _fixture;
        private readonly HttpClient _client;

        public AdminDeliveryFailuresApiTests(DeliveriesApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task List_failures_returns_only_failed_deliveries_with_paging_metadata()
        {
            var now = DateTimeOffset.UtcNow;
            var customerId = "cust-failures";
            var contractId = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO customers(id, name, enabled, created_at, updated_at)
                VALUES (@customerId, 'Failures Customer', TRUE, @now, @now);
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

            await InsertDeliveryAsync(conn, contractId, customerId, "Failed", now.AddMinutes(-3));
            await InsertDeliveryAsync(conn, contractId, customerId, "Failed", now.AddMinutes(-2));
            await InsertDeliveryAsync(conn, contractId, customerId, "Dead", now.AddMinutes(-1));

            var response = await _client.GetAsync($"/v1/admin/deliveries/failures?customerId={customerId}&page=1&pageSize=1&sortBy=createdAt&sortDirection=desc");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<PagedResponse<FailedDeliveryView>>();
            Assert.NotNull(body);
            Assert.Equal(1, body!.Page);
            Assert.Equal(1, body.PageSize);
            Assert.Equal(2L, body.TotalCount);
            Assert.Equal(2, body.TotalPages);
            Assert.True(body.HasNext);
            Assert.False(body.HasPrevious);
            Assert.Single(body.Items);
            Assert.Equal("cust-failures", body.Items[0].CustomerId);
            Assert.Equal(contractId, body.Items[0].ContractId);
        }

        [Fact]
        public async Task List_failures_returns_400_for_invalid_sort_field()
        {
            var response = await _client.GetAsync("/v1/admin/deliveries/failures?sortBy=invalidField");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.NotNull(error);
            Assert.Equal("validation", error!.Code);
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
                    last_error,
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
                    2,
                    'network error',
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

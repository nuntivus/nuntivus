using System.Net;
using System.Net.Http.Json;
using Dapper;
using KairoServer.Api.Controllers.Deliveries;
using KairoServer.Api.Common;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(DeliveriesApiCollection))]
    public sealed class DeliveriesApiTests : IAsyncLifetime
    {
        private readonly DeliveriesApiFixture _fixture;
        private readonly HttpClient _client;

        public DeliveriesApiTests(DeliveriesApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Operator-Id", "operator-api");
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Retry_endpoint_returns_200_and_is_idempotent()
        {
            var id = await SeedDeadDeliveryAsync();
            var request = new ManualDeliveryOperationApiRequest(Guid.NewGuid(), "retry now");

            var first = await _client.PostAsJsonAsync($"/v1/deliveries/{id}/retry", request);
            var second = await _client.PostAsJsonAsync($"/v1/deliveries/{id}/retry", request);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var status = await conn.ExecuteScalarAsync<string>("SELECT status FROM deliveries WHERE id = @id", new { id });
            var audits = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM delivery_operation_audits WHERE delivery_id = @id", new { id });

            Assert.Equal("Pending", status);
            Assert.Equal(1L, audits);
        }

        [Fact]
        public async Task Cancel_endpoint_transitions_failed_to_dead()
        {
            var id = await SeedFailedDeliveryAsync();
            var response = await _client.PostAsJsonAsync(
                $"/v1/deliveries/{id}/cancel",
                new ManualDeliveryOperationApiRequest(Guid.NewGuid(), "operator canceled"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var row = await conn.QuerySingleAsync<(string status, string? last_error)>(
                "SELECT status, last_error FROM deliveries WHERE id = @id", new { id });

            Assert.Equal("Dead", row.status);
            Assert.Contains("operator canceled", row.last_error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Inspect_endpoints_return_failure_and_attempts()
        {
            var id = await SeedDeadDeliveryWithAttemptAsync();

            var failure = await _client.GetAsync($"/v1/deliveries/{id}/failure");
            var attempts = await _client.GetAsync($"/v1/deliveries/{id}/attempts");

            Assert.Equal(HttpStatusCode.OK, failure.StatusCode);
            Assert.Equal(HttpStatusCode.OK, attempts.StatusCode);

            var failureBody = await failure.Content.ReadFromJsonAsync<DeliveryFailureDto>();
            var attemptsBody = await attempts.Content.ReadFromJsonAsync<List<DeliveryAttemptDto>>();

            Assert.NotNull(failureBody);
            Assert.Equal(id, failureBody!.DeliveryId);
            Assert.NotNull(attemptsBody);
            Assert.Single(attemptsBody!);
            Assert.False(attemptsBody[0].Succeeded);
        }

        [Fact]
        public async Task Manual_operation_requires_authenticated_principal()
        {
            using var unauthClient = _fixture.CreateClient();
            var id = await SeedDeadDeliveryAsync();

            var response = await unauthClient.PostAsJsonAsync(
                $"/v1/deliveries/{id}/requeue",
                new ManualDeliveryOperationApiRequest(Guid.NewGuid(), "requeue"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var err = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("unauthorized", err!.Code);
        }

        [Fact]
        public async Task Replay_job_endpoint_is_idempotent_by_operation_id()
        {
            await SeedDeadDeliveryAsync();
            var operationId = Guid.NewGuid();
            var request = new DeliveryReplayJobApiRequest(
                operationId,
                EventType: "orders.created",
                CustomerId: null,
                ContractId: null,
                CreatedFrom: DateTimeOffset.UtcNow.AddHours(-1),
                CreatedTo: DateTimeOffset.UtcNow.AddHours(1),
                BatchLimit: 10,
                Reason: "manual replay");

            var first = await _client.PostAsJsonAsync("/v1/deliveries/replay-jobs", request);
            var second = await _client.PostAsJsonAsync("/v1/deliveries/replay-jobs", request);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            var firstBody = await first.Content.ReadFromJsonAsync<DeliveryReplayJobDto>();
            var secondBody = await second.Content.ReadFromJsonAsync<DeliveryReplayJobDto>();

            Assert.NotNull(firstBody);
            Assert.NotNull(secondBody);
            Assert.Equal(firstBody!.Id, secondBody!.Id);
            Assert.Equal("Pending", firstBody.Status);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM delivery_replay_jobs WHERE operation_id = @operationId;",
                new { operationId });

            Assert.Equal(1L, count);
        }

        private async Task<Guid> SeedDeadDeliveryAsync()
        {
            var now = DateTimeOffset.UtcNow;
            var deliveryId = Guid.NewGuid();
            var (eventId, contractId, customerId) = await SeedGraphAsync(now);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                """
                INSERT INTO deliveries
                (id, event_id, contract_id, customer_id, event_type, payload, destination_type, destination_config, status, attempt_count, last_error, next_attempt_at, created_at, updated_at)
                VALUES (@id, @eventId, @contractId, @customerId, 'orders.created', '{}'::jsonb, 'Http', '{"url":"https://example.test"}'::jsonb, 'Dead', 1, 'final failure', NULL, @now, @now);
                """,
                new { id = deliveryId, eventId, contractId, customerId, now });

            return deliveryId;
        }

        private async Task<Guid> SeedFailedDeliveryAsync()
        {
            var now = DateTimeOffset.UtcNow;
            var deliveryId = Guid.NewGuid();
            var (eventId, contractId, customerId) = await SeedGraphAsync(now);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                """
                INSERT INTO deliveries
                (id, event_id, contract_id, customer_id, event_type, payload, destination_type, destination_config, status, attempt_count, last_error, next_attempt_at, created_at, updated_at)
                VALUES (@id, @eventId, @contractId, @customerId, 'orders.created', '{}'::jsonb, 'Http', '{"url":"https://example.test"}'::jsonb, 'Failed', 2, 'temporary failure', @nextAttemptAt, @now, @now);
                """,
                new { id = deliveryId, eventId, contractId, customerId, nextAttemptAt = now.AddMinutes(1), now });

            return deliveryId;
        }

        private async Task<Guid> SeedDeadDeliveryWithAttemptAsync()
        {
            var deliveryId = await SeedDeadDeliveryAsync();
            var now = DateTimeOffset.UtcNow;

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                """
                INSERT INTO delivery_attempts
                (id, delivery_id, attempt_number, succeeded, failure_category, error, metadata, started_at, completed_at)
                VALUES (@id, @deliveryId, 1, FALSE, 'Permanent', 'failed', '{"http_status":"500"}'::jsonb, @start, @end);
                """,
                new { id = Guid.NewGuid(), deliveryId, start = now.AddSeconds(-5), end = now });

            return deliveryId;
        }

        private async Task<(Guid eventId, Guid contractId, string customerId)> SeedGraphAsync(DateTimeOffset now)
        {
            var customerId = "cust-" + Guid.NewGuid().ToString("N")[..6];
            var contractId = Guid.NewGuid();
            var eventId = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                "INSERT INTO customers(id, name, enabled, created_at, updated_at) VALUES (@id, @name, TRUE, @now, @now);",
                new { id = customerId, name = "Cust", now });

            await conn.ExecuteAsync(
                """
                INSERT INTO contracts(id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@id, @cid, TRUE, 'Http', '{"url":"https://example.test"}'::jsonb, @now, @now);
                """,
                new { id = contractId, cid = customerId, now });

            await conn.ExecuteAsync(
                """
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@id, 'orders.created', @idem, @src, @now, @now, '{}'::jsonb, '{}'::jsonb);
                """,
                new { id = eventId, idem = Guid.NewGuid().ToString("N"), src = Guid.NewGuid(), now });

            return (eventId, contractId, customerId);
        }

        private sealed record DeliveryFailureDto(Guid DeliveryId, string Status, string? LastError, DateTimeOffset? NextAttemptAt, DateTimeOffset UpdatedAt);
        private sealed record DeliveryAttemptDto(Guid Id, int AttemptNumber, bool Succeeded, string? FailureCategory, string? Error, Dictionary<string, string> Metadata, DateTimeOffset StartedAt, DateTimeOffset CompletedAt);
        private sealed record DeliveryReplayJobDto(Guid Id, string Status, int ReplayedCount);
    }
}

using System.Net;
using System.Net.Http.Json;
using Dapper;
using NuntivusServer.Api.Controllers.Deliveries;
using NuntivusServer.Api.Common;
using NuntivusServer.Application.Deliveries.Recovery;
using NuntivusServer.Domain.Deliveries;
using Npgsql;

namespace NuntivusServer.IntegrationTests
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
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
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
            var genericAudits = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE category = 'delivery' AND action = 'retry' AND delivery_id = @id;",
                new { id });

            Assert.Equal("Pending", status);
            Assert.Equal(1L, audits);
            Assert.Equal(1L, genericAudits);
        }

        [Fact]
        public async Task Retry_audit_log_persists_request_and_response_snapshots()
        {
            var id = await SeedDeadDeliveryAsync();
            var operationId = Guid.NewGuid();
            var request = new ManualDeliveryOperationApiRequest(operationId, "retry with snapshots");

            var response = await _client.PostAsJsonAsync($"/v1/deliveries/{id}/retry", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var hasSnapshots = await conn.ExecuteScalarAsync<bool>(
                """
                SELECT (details::jsonb ? 'request') AND (details::jsonb ? 'response')
                FROM audit_logs
                WHERE category = 'delivery' AND action = 'retry' AND delivery_id = @id
                ORDER BY created_at DESC
                LIMIT 1;
                """,
                new { id });

            Assert.True(hasSnapshots);
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
        public async Task Cancel_endpoint_transitions_pending_to_dead_and_is_idempotent_for_dead()
        {
            var now = DateTimeOffset.UtcNow;
            var (eventId, contractId, customerId) = await SeedGraphAsync(now.AddMinutes(-2));
            var id = Guid.NewGuid();

            await InsertDeliveryAsync(
                id,
                eventId,
                contractId,
                customerId,
                status: "Pending",
                attemptCount: 0,
                createdAt: now.AddMinutes(-2),
                updatedAt: now.AddMinutes(-2));

            var operationId = Guid.NewGuid();
            var first = await _client.PostAsJsonAsync(
                $"/v1/deliveries/{id}/cancel",
                new ManualDeliveryOperationApiRequest(operationId, "cancel pending"));
            var second = await _client.PostAsJsonAsync(
                $"/v1/deliveries/{id}/cancel",
                new ManualDeliveryOperationApiRequest(operationId, "cancel pending"));

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var status = await conn.ExecuteScalarAsync<string>("SELECT status FROM deliveries WHERE id = @id", new { id });
            var audits = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM delivery_operation_audits WHERE delivery_id = @id", new { id });

            Assert.Equal("Dead", status);
            Assert.Equal(1L, audits);
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
            Assert.Equal("Permanent", attemptsBody[0].FailureCategory);
            Assert.True(attemptsBody[0].DurationMs > 0);
            Assert.NotEqual(default, attemptsBody[0].StartedAt);
            Assert.NotEqual(default, attemptsBody[0].CompletedAt);

            Assert.Equal("Permanent", failureBody.FailureCategory);
            Assert.NotNull(failureBody.FailedAt);
        }

        [Fact]
        public async Task Failure_endpoint_returns_structured_state_for_failed_pending_and_succeeded_deliveries()
        {
            var now = DateTimeOffset.UtcNow;
            var (eventId, contractId, customerId) = await SeedGraphAsync(now.AddMinutes(-10));
            var pendingEventId = await SeedEventAsync(now.AddMinutes(-4));
            var deliveredEventId = await SeedEventAsync(now.AddMinutes(-3));

            var failedId = Guid.NewGuid();
            await InsertDeliveryAsync(
                failedId,
                eventId,
                contractId,
                customerId,
                status: "Failed",
                attemptCount: 2,
                createdAt: now.AddMinutes(-9),
                updatedAt: now.AddMinutes(-5),
                lastError: "timeout",
                nextAttemptAt: now.AddMinutes(2));
            await InsertAttemptAsync(
                failedId,
                attemptNumber: 2,
                succeeded: false,
                failureCategory: "Transient",
                error: "timeout",
                startedAt: now.AddMinutes(-5),
                completedAt: now.AddMinutes(-5).AddSeconds(4));

            var pendingId = Guid.NewGuid();
            await InsertDeliveryAsync(
                pendingId,
                pendingEventId,
                contractId,
                customerId,
                status: "Pending",
                attemptCount: 0,
                createdAt: now.AddMinutes(-4),
                updatedAt: now.AddMinutes(-4));

            var deliveredId = Guid.NewGuid();
            await InsertDeliveryAsync(
                deliveredId,
                deliveredEventId,
                contractId,
                customerId,
                status: "Delivered",
                attemptCount: 1,
                createdAt: now.AddMinutes(-3),
                updatedAt: now.AddMinutes(-2));

            var failedResponse = await _client.GetFromJsonAsync<DeliveryFailureDto>($"/v1/deliveries/{failedId}/failure");
            var pendingResponse = await _client.GetFromJsonAsync<DeliveryFailureDto>($"/v1/deliveries/{pendingId}/failure");
            var deliveredResponse = await _client.GetFromJsonAsync<DeliveryFailureDto>($"/v1/deliveries/{deliveredId}/failure");

            Assert.NotNull(failedResponse);
            Assert.Equal(DeliveryStatus.Failed, failedResponse!.Status);
            Assert.Equal("Transient", failedResponse.FailureCategory);
            Assert.NotNull(failedResponse.FailedAt);
            Assert.NotNull(failedResponse.NextAttemptAt);

            Assert.NotNull(pendingResponse);
            Assert.Equal(DeliveryStatus.Pending, pendingResponse!.Status);
            Assert.Null(pendingResponse.FailureCategory);
            Assert.Null(pendingResponse.FailedAt);

            Assert.NotNull(deliveredResponse);
            Assert.Equal(DeliveryStatus.Delivered, deliveredResponse!.Status);
            Assert.Null(deliveredResponse.FailureCategory);
            Assert.Null(deliveredResponse.FailedAt);
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
            Assert.Equal(DeliveryReplayJobStatus.Pending, firstBody.Status);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM delivery_replay_jobs WHERE operation_id = @operationId;",
                new { operationId });
            var auditCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE category = 'delivery' AND action = 'replay_requested' AND operation_id = @operationId;",
                new { operationId });

            Assert.Equal(1L, count);
            Assert.Equal(1L, auditCount);
        }

        [Fact]
        public async Task Replay_job_endpoint_rejects_invalid_batch_limit()
        {
            var request = new DeliveryReplayJobApiRequest(
                Guid.NewGuid(),
                EventType: "orders.created",
                CustomerId: null,
                ContractId: null,
                CreatedFrom: DateTimeOffset.UtcNow.AddHours(-1),
                CreatedTo: DateTimeOffset.UtcNow,
                BatchLimit: 0,
                Reason: "invalid");

            var response = await _client.PostAsJsonAsync("/v1/deliveries/replay-jobs", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.NotNull(error);
            Assert.Equal("validation", error!.Code);
        }

        [Fact]
        public async Task Replay_job_endpoint_requires_authentication()
        {
            using var unauthClient = _fixture.CreateClient();

            var request = new DeliveryReplayJobApiRequest(
                Guid.NewGuid(),
                EventType: "orders.created",
                CustomerId: null,
                ContractId: null,
                CreatedFrom: DateTimeOffset.UtcNow.AddHours(-1),
                CreatedTo: DateTimeOffset.UtcNow,
                BatchLimit: 10,
                Reason: "auth");

            var response = await unauthClient.PostAsJsonAsync("/v1/deliveries/replay-jobs", request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Get_delivery_endpoint_returns_state_history_and_latest_failure()
        {
            var now = DateTimeOffset.UtcNow;
            var (eventId, contractId, customerId) = await SeedGraphAsync(now.AddMinutes(-10));
            var deliveryId = Guid.NewGuid();

            await InsertDeliveryAsync(
                deliveryId,
                eventId,
                contractId,
                customerId,
                status: "Failed",
                attemptCount: 2,
                createdAt: now.AddMinutes(-9),
                updatedAt: now.AddMinutes(-5),
                lastError: "delivery failed",
                nextAttemptAt: now.AddMinutes(1));

            await InsertAttemptAsync(
                deliveryId,
                1,
                succeeded: false,
                failureCategory: "Transient",
                error: "timeout",
                startedAt: now.AddMinutes(-8),
                completedAt: now.AddMinutes(-8).AddSeconds(5));
            await InsertAttemptAsync(
                deliveryId,
                2,
                succeeded: false,
                failureCategory: "Permanent",
                error: "bad request",
                startedAt: now.AddMinutes(-6),
                completedAt: now.AddMinutes(-6).AddSeconds(5));

            var response = await _client.GetAsync($"/v1/deliveries/{deliveryId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<DeliveryQueryDto>();
            Assert.NotNull(body);
            Assert.Equal(deliveryId, body!.Id);
            Assert.Equal(eventId, body.EventId);
            Assert.Equal(contractId, body.ContractId);
            Assert.Equal(customerId, body.CustomerId);
            Assert.Equal(DeliveryStatus.Failed, body.Status);
            Assert.Equal(2, body.AttemptSummary.TotalAttempts);
            Assert.Equal(0, body.AttemptSummary.SuccessfulAttempts);
            Assert.Equal(2, body.AttemptSummary.FailedAttempts);
            Assert.NotNull(body.LatestFailure);
            Assert.Equal("Permanent", body.LatestFailure!.FailureCategory);
            Assert.Equal("bad request", body.LatestFailure.Error);
        }

        [Theory]
        [InlineData("linked")]
        [InlineData("detached")]
        [InlineData("deleted")]
        [InlineData("missing")]
        [InlineData("unbound")]
        public async Task Delivery_queries_resolve_only_the_recorded_destination_binding(string state)
        {
            var deliveryId = await SeedDeadDeliveryAsync();
            var destinationId = Guid.NewGuid();
            var otherDestinationId = Guid.NewGuid();
            var bindingId = Guid.NewGuid();
            var otherBindingId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var contractId = await conn.ExecuteScalarAsync<Guid>(
                "SELECT contract_id FROM deliveries WHERE id = @deliveryId;", new { deliveryId });
            await conn.ExecuteAsync(
                """
                INSERT INTO destinations(id, name, type, configuration, enabled, created_at, updated_at)
                VALUES (@otherDestinationId, 'Other destination', 'Http', '{}'::jsonb, TRUE, @now, @now),
                       (@destinationId, 'Recorded destination', 'Http', '{}'::jsonb, TRUE, @now, @now);
                INSERT INTO contract_destinations(id, contract_id, destination_id, destination_type, destination_config, created_at)
                VALUES (@otherBindingId, @contractId, @otherDestinationId, 'Http', '{}'::jsonb, @now),
                       (@bindingId, @contractId, @destinationId, 'Http', '{}'::jsonb, @now);
                """,
                new { destinationId, otherDestinationId, bindingId, otherBindingId, contractId, now });

            if (state != "unbound")
            {
                await conn.ExecuteAsync(
                    "UPDATE deliveries SET destination_binding_id = @bindingId WHERE id = @deliveryId;",
                    new { bindingId, deliveryId });
            }

            if (state == "detached")
                await conn.ExecuteAsync("UPDATE contract_destinations SET destination_id = NULL WHERE id = @bindingId;", new { bindingId });
            else if (state == "deleted")
                await conn.ExecuteAsync("DELETE FROM destinations WHERE id = @destinationId;", new { destinationId });
            else if (state == "missing")
                await conn.ExecuteAsync("DELETE FROM contract_destinations WHERE id = @bindingId;", new { bindingId });

            Guid? expectedDestinationId = state == "linked" ? destinationId : null;
            var detail = await _client.GetFromJsonAsync<DeliveryQueryDto>($"/v1/deliveries/{deliveryId}");
            Assert.NotNull(detail);
            Assert.Equal(expectedDestinationId, detail.DestinationId);

            var list = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>($"/v1/deliveries?contractId={contractId}");
            Assert.NotNull(list);
            var listed = Assert.Single(list.Items);
            Assert.Equal(deliveryId, listed.Id);
            Assert.Equal(expectedDestinationId, listed.DestinationId);
        }

        [Fact]
        public async Task List_deliveries_filters_by_event_customer_contract_status_and_time_range()
        {
            var now = DateTimeOffset.UtcNow;
            var (targetEventId, targetContractId, customerId) = await SeedGraphAsync(now.AddMinutes(-30));
            var otherEventId = await SeedEventAsync(now.AddMinutes(-30));
            var otherContractId = await SeedContractAsync(customerId, now.AddMinutes(-30));
            var thirdEventId = await SeedEventAsync(now.AddMinutes(-30));
            var thirdContractId = await SeedContractAsync(customerId, now.AddMinutes(-30));

            var matched = Guid.NewGuid();
            await InsertDeliveryAsync(
                matched,
                targetEventId,
                targetContractId,
                customerId,
                status: "Failed",
                attemptCount: 1,
                createdAt: now.AddMinutes(-20),
                updatedAt: now.AddMinutes(-19),
                lastError: "target");

            await InsertDeliveryAsync(
                Guid.NewGuid(),
                otherEventId,
                targetContractId,
                customerId,
                status: "Failed",
                attemptCount: 1,
                createdAt: now.AddMinutes(-20),
                updatedAt: now.AddMinutes(-19),
                lastError: "other event");

            await InsertDeliveryAsync(
                Guid.NewGuid(),
                targetEventId,
                otherContractId,
                customerId,
                status: "Failed",
                attemptCount: 1,
                createdAt: now.AddMinutes(-20),
                updatedAt: now.AddMinutes(-19),
                lastError: "other contract");

            await InsertDeliveryAsync(
                Guid.NewGuid(),
                thirdEventId,
                thirdContractId,
                customerId,
                status: "Delivered",
                attemptCount: 1,
                createdAt: now.AddMinutes(-20),
                updatedAt: now.AddMinutes(-19));

            var url = $"/v1/deliveries?eventId={targetEventId:D}&customerId={customerId}&contractId={targetContractId:D}&status=Failed&createdFrom={Uri.EscapeDataString(now.AddMinutes(-21).ToString("O"))}&createdTo={Uri.EscapeDataString(now.AddMinutes(-18).ToString("O"))}";
            var response = await _client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<PagedResponse<DeliveryQueryDto>>();
            Assert.NotNull(body);
            Assert.Single(body!.Items);
            Assert.Equal(matched, body.Items[0].Id);
        }

        [Fact]
        public async Task List_deliveries_supports_pagination_and_sorting()
        {
            var now = DateTimeOffset.UtcNow;
            var (eventId, contractId, customerId) = await SeedGraphAsync(now.AddMinutes(-30));
            var secondEventId = await SeedEventAsync(now.AddMinutes(-30));
            var thirdEventId = await SeedEventAsync(now.AddMinutes(-30));

            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();

            await InsertDeliveryAsync(first, eventId, contractId, customerId, "Pending", 0, now.AddMinutes(-25), now.AddMinutes(-24));
            await InsertDeliveryAsync(second, secondEventId, contractId, customerId, "Pending", 0, now.AddMinutes(-20), now.AddMinutes(-19));
            await InsertDeliveryAsync(third, thirdEventId, contractId, customerId, "Pending", 0, now.AddMinutes(-15), now.AddMinutes(-14));

            var page1 = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>(
                "/v1/deliveries?page=1&pageSize=2&sortBy=createdAt&sortDirection=asc");
            var page2 = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>(
                "/v1/deliveries?page=2&pageSize=2&sortBy=createdAt&sortDirection=asc");

            Assert.NotNull(page1);
            Assert.NotNull(page2);
            Assert.Equal(3L, page1!.TotalCount);
            Assert.Equal(2, page1.Items.Count);
            Assert.Equal(first, page1.Items[0].Id);
            Assert.Equal(second, page1.Items[1].Id);
            Assert.True(page1.HasNext);

            Assert.Single(page2!.Items);
            Assert.Equal(third, page2.Items[0].Id);
            Assert.False(page2.HasNext);
        }

        [Fact]
        public async Task List_failures_includes_failed_and_dead_with_matching_pagination_counts()
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(-10);
            var (eventId, contractId, customerId) = await SeedGraphAsync(now);
            var ids = new Dictionary<string, Guid>();
            var statuses = new[] { "Pending", "InFlight", "Delivered", "Failed", "Dead" };
            for (var index = 0; index < statuses.Length; index++)
            {
                var status = statuses[index];
                var id = Guid.NewGuid();
                ids[status] = id;
                var currentEventId = index == 0 ? eventId : await SeedEventAsync(now);
                await InsertDeliveryAsync(id, currentEventId, contractId, customerId, status, 0,
                    now.AddMinutes(index), now.AddMinutes(index));
            }

            var filter = $"/v1/deliveries?failuresOnly=true&contractId={contractId}&sortBy=createdAt&sortDirection=asc&pageSize=1";
            var first = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>($"{filter}&page=1");
            var second = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>($"{filter}&page=2");
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(2L, first.TotalCount);
            Assert.Equal(2L, second.TotalCount);
            Assert.Equal(ids["Failed"], Assert.Single(first.Items).Id);
            Assert.Equal(ids["Dead"], Assert.Single(second.Items).Id);
            Assert.True(first.HasNext);
            Assert.False(second.HasNext);

            foreach (var status in new[] { "Failed", "Dead" })
            {
                var narrowed = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>($"{filter}&status={status}");
                Assert.NotNull(narrowed);
                Assert.Equal(1L, narrowed.TotalCount);
                Assert.Equal(ids[status], Assert.Single(narrowed.Items).Id);
            }

            var excluded = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>($"{filter}&status=Delivered");
            Assert.NotNull(excluded);
            Assert.Equal(0L, excluded.TotalCount);
            Assert.Empty(excluded.Items);
        }

        [Fact]
        public async Task List_deliveries_searches_ids_and_event_types_with_matching_counts()
        {
            var now = DateTimeOffset.UtcNow;
            var (eventId, contractId, customerId) = await SeedGraphAsync(now);
            var secondEventId = await SeedEventAsync(now);
            var thirdEventId = await SeedEventAsync(now);
            var target = Guid.NewGuid();
            await InsertDeliveryAsync(Guid.NewGuid(), eventId, contractId, customerId, "Pending", 0, now, now);
            await InsertDeliveryAsync(Guid.NewGuid(), secondEventId, contractId, customerId, "Pending", 0, now, now);
            await InsertDeliveryAsync(target, thirdEventId, contractId, customerId, "Pending", 0, now, now);
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.ExecuteAsync("UPDATE deliveries SET event_type = 'invoices.paid' WHERE id = @Id;", new { Id = target });

            var page1 = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>("/v1/deliveries?search=ORDERS&pageSize=1&page=1");
            var page2 = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>("/v1/deliveries?search=ORDERS&pageSize=1&page=2");
            Assert.Equal(2L, page1!.TotalCount);
            Assert.True(page1.HasNext);
            Assert.Equal(2L, page2!.TotalCount);
            Assert.False(page2.HasNext);
            Assert.NotEqual(Assert.Single(page1.Items).Id, Assert.Single(page2.Items).Id);

            foreach (var search in new[] { target.ToString("D")[..8], "INVOICES" })
            {
                var result = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>($"/v1/deliveries?search={search}&customerId={customerId}&pageSize=1");
                Assert.Equal(1L, result!.TotalCount);
                Assert.Equal(target, Assert.Single(result.Items).Id);
            }
            var noMatches = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>($"/v1/deliveries?search={Uri.EscapeDataString("' OR 1=1 --")}");
            Assert.Equal(0L, noMatches!.TotalCount);
            Assert.Empty(noMatches.Items);
        }

        [Fact]
        public async Task List_deliveries_includes_attempt_summary_and_latest_failure()
        {
            var now = DateTimeOffset.UtcNow;
            var (eventId, contractId, customerId) = await SeedGraphAsync(now.AddMinutes(-10));
            var deliveryId = Guid.NewGuid();

            await InsertDeliveryAsync(
                deliveryId,
                eventId,
                contractId,
                customerId,
                status: "Dead",
                attemptCount: 3,
                createdAt: now.AddMinutes(-9),
                updatedAt: now.AddMinutes(-2),
                lastError: "failed");

            await InsertAttemptAsync(deliveryId, 1, false, "Transient", "timeout", now.AddMinutes(-8), now.AddMinutes(-8).AddSeconds(5));
            await InsertAttemptAsync(deliveryId, 2, true, null, null, now.AddMinutes(-6), now.AddMinutes(-6).AddSeconds(5));
            await InsertAttemptAsync(deliveryId, 3, false, "Permanent", "invalid payload", now.AddMinutes(-3), now.AddMinutes(-3).AddSeconds(5));

            var body = await _client.GetFromJsonAsync<PagedResponse<DeliveryQueryDto>>(
                $"/v1/deliveries?eventId={eventId:D}&contractId={contractId:D}&customerId={customerId}");

            Assert.NotNull(body);
            Assert.Single(body!.Items);

            var item = body.Items[0];
            Assert.Equal(deliveryId, item.Id);
            Assert.Equal(3, item.AttemptSummary.TotalAttempts);
            Assert.Equal(1, item.AttemptSummary.SuccessfulAttempts);
            Assert.Equal(2, item.AttemptSummary.FailedAttempts);
            Assert.NotNull(item.LatestFailure);
            Assert.Equal("Permanent", item.LatestFailure!.FailureCategory);
            Assert.Equal("invalid payload", item.LatestFailure.Error);
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

        private async Task<Guid> SeedEventAsync(DateTimeOffset now)
        {
            var eventId = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@id, 'orders.created', @idem, @src, @now, @now, '{}'::jsonb, '{}'::jsonb);
                """,
                new { id = eventId, idem = Guid.NewGuid().ToString("N"), src = Guid.NewGuid(), now });

            return eventId;
        }

        private async Task<Guid> SeedContractAsync(string customerId, DateTimeOffset now)
        {
            var contractId = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO contracts(id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@id, @cid, TRUE, 'Http', '{"url":"https://example.test"}'::jsonb, @now, @now);
                """,
                new { id = contractId, cid = customerId, now });

            return contractId;
        }

        private async Task InsertDeliveryAsync(
            Guid id,
            Guid eventId,
            Guid contractId,
            string customerId,
            string status,
            int attemptCount,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            string? lastError = null,
            DateTimeOffset? nextAttemptAt = null)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO deliveries
                (id, event_id, contract_id, customer_id, event_type, payload, destination_type, destination_config, status, attempt_count, last_error, next_attempt_at, created_at, updated_at)
                VALUES (@id, @eventId, @contractId, @customerId, 'orders.created', '{}'::jsonb, 'Http', '{"url":"https://example.test"}'::jsonb, @status, @attemptCount, @lastError, @nextAttemptAt, @createdAt, @updatedAt);
                """,
                new
                {
                    id,
                    eventId,
                    contractId,
                    customerId,
                    status,
                    attemptCount,
                    lastError,
                    nextAttemptAt,
                    createdAt,
                    updatedAt,
                });
        }

        private async Task InsertAttemptAsync(
            Guid deliveryId,
            int attemptNumber,
            bool succeeded,
            string? failureCategory,
            string? error,
            DateTimeOffset startedAt,
            DateTimeOffset completedAt)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO delivery_attempts
                (id, delivery_id, attempt_number, succeeded, failure_category, error, metadata, started_at, completed_at)
                VALUES (@id, @deliveryId, @attemptNumber, @succeeded, @failureCategory, @error, '{}'::jsonb, @startedAt, @completedAt);
                """,
                new
                {
                    id = Guid.NewGuid(),
                    deliveryId,
                    attemptNumber,
                    succeeded,
                    failureCategory,
                    error,
                    startedAt,
                    completedAt,
                });
        }

        private sealed record DeliveryFailureDto(Guid DeliveryId, DeliveryStatus Status, string? LastError, string? FailureCategory, DateTimeOffset? FailedAt, DateTimeOffset? NextAttemptAt, DateTimeOffset UpdatedAt);
        private sealed record DeliveryAttemptDto(Guid Id, int AttemptNumber, bool Succeeded, string? FailureCategory, string? Error, Dictionary<string, string> Metadata, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, long DurationMs);
        private sealed record DeliveryReplayJobDto(Guid Id, DeliveryReplayJobStatus Status, int ReplayedCount);
        private sealed record DeliveryQueryDto(
            Guid Id,
            Guid EventId,
            string CustomerId,
            Guid ContractId,
            string EventType,
            DeliveryStatus Status,
            int AttemptCount,
            string? LastError,
            DateTimeOffset? NextAttemptAt,
            DateTimeOffset CreatedAt,
            DateTimeOffset UpdatedAt,
            DeliveryAttemptSummaryDto AttemptSummary,
            DeliveryLatestFailureDto? LatestFailure,
            Guid? DestinationId);
        private sealed record DeliveryAttemptSummaryDto(
            int TotalAttempts,
            int SuccessfulAttempts,
            int FailedAttempts,
            DateTimeOffset? LastAttemptAt);
        private sealed record DeliveryLatestFailureDto(
            string? FailureCategory,
            string? Error,
            DateTimeOffset? FailedAt);
    }
}

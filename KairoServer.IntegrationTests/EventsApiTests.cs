using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Api.Common;
using KairoServer.Api.Controllers.Events;
using KairoServer.Api.Controllers.Sources;
using KairoServer.Domain.Outbox;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(EventsApiCollection))]
    public sealed class EventsApiTests : IAsyncLifetime
    {
        private readonly EventsApiFixture _fixture;
        private readonly HttpClient _client;

        public EventsApiTests(EventsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Replay_existing_event_creates_new_event_and_tracks_lineage()
        {
            var sourceId = await SeedEventAsync();

            var resp = await _client.PostAsJsonAsync(
                $"/v1/events/{sourceId:D}/replay",
                new ReplayEventRequest("ops-replay-1", "manual replay"));

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ReplayEventResponse>();
            Assert.NotNull(body);
            Assert.Equal("accepted", body!.Status);
            Assert.Equal(sourceId.ToString("D"), body.SourceEventId);

            var replayedEventId = Guid.Parse(body.ReplayedEventId);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var replayRows = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM event_replays WHERE source_event_id = @source AND replayed_event_id = @replayed;",
                new { source = sourceId, replayed = replayedEventId });
            Assert.Equal(1, replayRows);

            var outboxRows = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM outbox_messages WHERE event_id = @eventId AND type = 'EventAccepted';",
                new { eventId = replayedEventId });
            Assert.Equal(1, outboxRows);

            var auditRows = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE category = 'event' AND action = 'replayed' AND event_id = @source;",
                new { source = sourceId });
            Assert.Equal(1, auditRows);
        }

        [Fact]
        public async Task Replay_same_key_is_idempotent_and_does_not_duplicate()
        {
            var sourceId = await SeedEventAsync();

            var first = await _client.PostAsJsonAsync(
                $"/v1/events/{sourceId:D}/replay",
                new ReplayEventRequest("ops-replay-2", null));
            var second = await _client.PostAsJsonAsync(
                $"/v1/events/{sourceId:D}/replay",
                new ReplayEventRequest("ops-replay-2", null));

            var firstBody = await first.Content.ReadFromJsonAsync<ReplayEventResponse>();
            var secondBody = await second.Content.ReadFromJsonAsync<ReplayEventResponse>();

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal("accepted", firstBody!.Status);
            Assert.Equal("already_replayed", secondBody!.Status);
            Assert.Equal(firstBody.ReplayedEventId, secondBody.ReplayedEventId);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var replayRows = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM event_replays WHERE source_event_id = @source AND replay_key = @key;",
                new { source = sourceId, key = "ops-replay-2" });
            Assert.Equal(1, replayRows);
        }

        [Fact]
        public async Task Replay_missing_event_returns_404()
        {
            var resp = await _client.PostAsJsonAsync(
                $"/v1/events/{Guid.NewGuid():D}/replay",
                new ReplayEventRequest("ops-replay-missing", null));

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("not_found", err!.Code);
        }

        [Fact]
        public async Task Publish_with_oversized_payload_returns_400()
        {
            using var payloadDoc = JsonDocument.Parse($"{{\"blob\":\"{new string('x', 300_000)}\"}}");

            var response = await _client.PostAsJsonAsync(
                "/v1/events",
                new PublishEventRequest(
                    IdempotencyKey: "idem-security-1",
                    Type: "orders.created",
                    Source: "orders-api",
                    Timestamp: DateTimeOffset.UtcNow,
                    Metadata: null,
                    Payload: payloadDoc.RootElement.Clone()));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("validation", error!.Code);
            Assert.Contains("payload exceeds", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Publish_with_oversized_metadata_returns_400()
        {
            using var metadataDoc = JsonDocument.Parse($"{{\"blob\":\"{new string('x', 80_000)}\"}}");

            var response = await _client.PostAsJsonAsync(
                "/v1/events",
                new PublishEventRequest(
                    IdempotencyKey: "idem-security-metadata-1",
                    Type: "orders.created",
                    Source: "orders-api",
                    Timestamp: DateTimeOffset.UtcNow,
                    Metadata: metadataDoc.RootElement.Clone(),
                    Payload: JsonDocument.Parse("{\"orderId\":\"o-1\"}").RootElement.Clone()));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("validation", error!.Code);
            Assert.Contains("metadata exceeds", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Publish_same_source_and_idempotency_key_is_idempotent()
        {
            var source = Guid.NewGuid().ToString("D");
            var idempotencyKey = "idem-publish-1";
            var occurredAt = DateTimeOffset.UtcNow;

            var first = await _client.PostAsJsonAsync(
                "/v1/events",
                new PublishEventRequest(
                    IdempotencyKey: idempotencyKey,
                    Type: "orders.created",
                    Source: source,
                    Timestamp: occurredAt,
                    Metadata: null,
                    Payload: JsonDocument.Parse("{\"orderId\":\"o-42\"}").RootElement.Clone()));

            var second = await _client.PostAsJsonAsync(
                "/v1/events",
                new PublishEventRequest(
                    IdempotencyKey: idempotencyKey,
                    Type: "orders.created",
                    Source: source,
                    Timestamp: occurredAt,
                    Metadata: null,
                    Payload: JsonDocument.Parse("{\"orderId\":\"o-42\"}").RootElement.Clone()));

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            var firstBody = await first.Content.ReadFromJsonAsync<PublishEventResponse>();
            var secondBody = await second.Content.ReadFromJsonAsync<PublishEventResponse>();
            Assert.NotNull(firstBody);
            Assert.NotNull(secondBody);
            Assert.Equal(firstBody!.Id, secondBody!.Id);
        }

        [Fact]
        public async Task Publish_persists_event_and_outbox_before_client_restart()
        {
            var source = Guid.NewGuid().ToString("D");
            var idempotencyKey = $"idem-durable-{Guid.NewGuid():N}";

            var response = await _client.PostAsJsonAsync(
                "/v1/events",
                new PublishEventRequest(
                    IdempotencyKey: idempotencyKey,
                    Type: "orders.created",
                    Source: source,
                    Timestamp: DateTimeOffset.UtcNow,
                    Metadata: null,
                    Payload: JsonDocument.Parse("{\"orderId\":\"o-durable\"}").RootElement.Clone()));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var ack = await response.Content.ReadFromJsonAsync<PublishEventResponse>();
            Assert.NotNull(ack);

            var eventId = Guid.Parse(ack!.Id);

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                var eventRows = await conn.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM events WHERE id = @id;",
                    new { id = eventId });
                var outboxRows = await conn.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM outbox_messages WHERE event_id = @id AND type = @type;",
                    new { id = eventId, type = OutboxMessageType.EventAccepted });

                Assert.Equal(1, eventRows);
                Assert.Equal(1, outboxRows);
            }

            using var restartedClient = _fixture.CreateClient();
            var getResponse = await restartedClient.GetAsync($"/v1/events/{eventId:D}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        }

        [Fact]
        public async Task Publish_normalizes_source_identity_before_idempotency_check()
        {
            var sourceGuid = Guid.NewGuid();
            var sourceUpper = sourceGuid.ToString("D").ToUpperInvariant();
            var sourceLower = sourceGuid.ToString("D");
            var idempotencyKey = "idem-publish-normalized-source-1";
            var occurredAt = DateTimeOffset.UtcNow;

            var first = await _client.PostAsJsonAsync(
                "/v1/events",
                new PublishEventRequest(
                    IdempotencyKey: idempotencyKey,
                    Type: "orders.created",
                    Source: $"  {sourceUpper}  ",
                    Timestamp: occurredAt,
                    Metadata: null,
                    Payload: JsonDocument.Parse("{\"orderId\":\"o-42\"}").RootElement.Clone()));

            var second = await _client.PostAsJsonAsync(
                "/v1/events",
                new PublishEventRequest(
                    IdempotencyKey: idempotencyKey,
                    Type: "orders.created",
                    Source: sourceLower,
                    Timestamp: occurredAt,
                    Metadata: null,
                    Payload: JsonDocument.Parse("{\"orderId\":\"o-42\"}").RootElement.Clone()));

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            var firstBody = await first.Content.ReadFromJsonAsync<PublishEventResponse>();
            var secondBody = await second.Content.ReadFromJsonAsync<PublishEventResponse>();
            Assert.NotNull(firstBody);
            Assert.NotNull(secondBody);
            Assert.Equal(firstBody!.Id, secondBody!.Id);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var persistedSource = await conn.ExecuteScalarAsync<string>(
                "SELECT source_id::text FROM events WHERE id = @id;",
                new { id = Guid.Parse(firstBody.Id) });

            Assert.Equal(sourceLower, persistedSource);
        }

        [Fact]
        public async Task Generic_webhook_ingest_accepts_and_is_idempotent_for_same_source_and_key()
        {
            var source = Guid.NewGuid().ToString("D");
            var idempotencyKey = "webhook-idem-1";
            var occurredAt = DateTimeOffset.UtcNow;

            var first = await _client.PostAsJsonAsync(
                $"/v1/sources/webhooks/{source}",
                new WebhookIngestRequest(
                    IdempotencyKey: idempotencyKey,
                    Type: "orders.webhook.created",
                    Timestamp: occurredAt,
                    Metadata: JsonDocument.Parse("{\"traceId\":\"w-1\"}").RootElement.Clone(),
                    Payload: JsonDocument.Parse("{\"orderId\":\"o-900\"}").RootElement.Clone()));

            var second = await _client.PostAsJsonAsync(
                $"/v1/sources/webhooks/{source}",
                new WebhookIngestRequest(
                    IdempotencyKey: idempotencyKey,
                    Type: "orders.webhook.created",
                    Timestamp: occurredAt,
                    Metadata: JsonDocument.Parse("{\"traceId\":\"w-1\"}").RootElement.Clone(),
                    Payload: JsonDocument.Parse("{\"orderId\":\"o-900\"}").RootElement.Clone()));

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            var firstBody = await first.Content.ReadFromJsonAsync<PublishEventResponse>();
            var secondBody = await second.Content.ReadFromJsonAsync<PublishEventResponse>();
            Assert.NotNull(firstBody);
            Assert.NotNull(secondBody);
            Assert.Equal(firstBody!.Id, secondBody!.Id);
        }

        [Fact]
        public async Task Generic_webhook_ingest_supports_header_fallback_for_idempotency_and_type()
        {
            var source = Guid.NewGuid().ToString("D");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/sources/webhooks/{source}");
            request.Headers.Add("X-Kairo-Idempotency-Key", "webhook-idem-header-1");
            request.Headers.Add("X-Kairo-Event-Type", "orders.webhook.header");
            request.Content = JsonContent.Create(new WebhookIngestRequest(
                IdempotencyKey: null,
                Type: null,
                Timestamp: DateTimeOffset.UtcNow,
                Metadata: null,
                Payload: JsonDocument.Parse("{\"orderId\":\"o-901\"}").RootElement.Clone()));

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<PublishEventResponse>();
            Assert.NotNull(body);
            Assert.Equal("accepted", body!.Status);
        }

        [Fact]
        public async Task Get_event_by_id_includes_related_deliveries_and_preserves_payload_and_metadata()
        {
            var now = DateTimeOffset.UtcNow;
            var sourceId = Guid.NewGuid();
            var eventId = await InsertEventAsync(
                sourceId,
                "orders.created",
                occurredAt: now.AddMinutes(-10),
                receivedAt: now.AddMinutes(-9),
                payload: """{"orderId":"o-100","amount":42,"items":["a","b"]}""",
                metadata: """{"traceId":"t-1","tenant":"north"}""");

            var customerId = await InsertCustomerAsync("cust-events-get", now.AddMinutes(-20));
            var contractId = await InsertContractAsync(customerId, now.AddMinutes(-20));

            var firstDeliveryId = Guid.NewGuid();
            await InsertDeliveryAsync(
                firstDeliveryId,
                eventId,
                contractId,
                customerId,
                status: "Failed",
                attemptCount: 2,
                createdAt: now.AddMinutes(-8),
                updatedAt: now.AddMinutes(-7),
                lastError: "failed once");
            await InsertAttemptAsync(firstDeliveryId, 1, succeeded: false, "Transient", "timeout", now.AddMinutes(-8), now.AddMinutes(-8).AddSeconds(3));
            await InsertAttemptAsync(firstDeliveryId, 2, succeeded: false, "Permanent", "bad request", now.AddMinutes(-7), now.AddMinutes(-7).AddSeconds(3));

            await InsertDeliveryAsync(
                Guid.NewGuid(),
                eventId,
                contractId,
                customerId,
                status: "Delivered",
                attemptCount: 1,
                createdAt: now.AddMinutes(-6),
                updatedAt: now.AddMinutes(-5));

            var response = await _client.GetAsync($"/v1/events/{eventId:D}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<EventQueryDto>();
            Assert.NotNull(body);
            Assert.Equal(eventId, body!.Id);
            Assert.Equal("orders.created", body.Type);
            Assert.Equal(sourceId.ToString("D"), body.Source);
            Assert.Equal("o-100", body.Payload.GetProperty("orderId").GetString());
            Assert.Equal(42, body.Payload.GetProperty("amount").GetInt32());
            Assert.Equal("t-1", body.Metadata!.Value.GetProperty("traceId").GetString());
            Assert.Equal(2, body.Deliveries.Count);

            var failedDelivery = Assert.Single(body.Deliveries, d => d.Id == firstDeliveryId);
            Assert.Equal("Failed", failedDelivery.Status);
            Assert.Equal(2, failedDelivery.AttemptSummary.TotalAttempts);
            Assert.Equal("Permanent", failedDelivery.LatestFailure!.FailureCategory);
        }

        [Fact]
        public async Task List_events_filters_by_type_source_occurred_and_received_and_supports_pagination()
        {
            var now = DateTimeOffset.UtcNow;
            var sourceA = Guid.NewGuid();
            var sourceB = Guid.NewGuid();

            var firstMatched = await InsertEventAsync(
                sourceA,
                "orders.created",
                occurredAt: now.AddMinutes(-30),
                receivedAt: now.AddMinutes(-29),
                payload: """{"id":"e1"}""",
                metadata: """{"kind":"match"}""");
            var secondMatched = await InsertEventAsync(
                sourceA,
                "orders.created",
                occurredAt: now.AddMinutes(-20),
                receivedAt: now.AddMinutes(-19),
                payload: """{"id":"e2"}""",
                metadata: "{}"
            );

            await InsertEventAsync(
                sourceA,
                "orders.cancelled",
                occurredAt: now.AddMinutes(-20),
                receivedAt: now.AddMinutes(-19),
                payload: """{"id":"e3"}""",
                metadata: "{}"
            );
            await InsertEventAsync(
                sourceB,
                "orders.created",
                occurredAt: now.AddMinutes(-20),
                receivedAt: now.AddMinutes(-19),
                payload: """{"id":"e4"}""",
                metadata: "{}"
            );

            var customerId = await InsertCustomerAsync("cust-events-list", now.AddMinutes(-60));
            var contractId = await InsertContractAsync(customerId, now.AddMinutes(-60));
            await InsertDeliveryAsync(Guid.NewGuid(), firstMatched, contractId, customerId, "Pending", 0, now.AddMinutes(-28), now.AddMinutes(-28));
            await InsertDeliveryAsync(Guid.NewGuid(), secondMatched, contractId, customerId, "Pending", 0, now.AddMinutes(-18), now.AddMinutes(-18));

            var commonQuery =
                $"type=orders.created&source={sourceA:D}" +
                $"&occurredFrom={Uri.EscapeDataString(now.AddMinutes(-31).ToString("O"))}" +
                $"&occurredTo={Uri.EscapeDataString(now.AddMinutes(-19).ToString("O"))}" +
                $"&receivedFrom={Uri.EscapeDataString(now.AddMinutes(-30).ToString("O"))}" +
                $"&receivedTo={Uri.EscapeDataString(now.AddMinutes(-18).ToString("O"))}";

            var page1 = await _client.GetFromJsonAsync<PagedResponse<EventQueryDto>>(
                $"/v1/events?{commonQuery}&sortBy=occurredAt&sortDirection=asc&page=1&pageSize=1");
            var page2 = await _client.GetFromJsonAsync<PagedResponse<EventQueryDto>>(
                $"/v1/events?{commonQuery}&sortBy=occurredAt&sortDirection=asc&page=2&pageSize=1");

            Assert.NotNull(page1);
            Assert.NotNull(page2);
            Assert.Equal(2L, page1!.TotalCount);
            Assert.Equal(2, page1.TotalPages);
            Assert.Single(page1.Items);
            Assert.Single(page2!.Items);
            Assert.Equal(firstMatched, page1.Items[0].Id);
            Assert.Equal(secondMatched, page2.Items[0].Id);
            Assert.NotEmpty(page1.Items[0].Deliveries);
        }

        [Theory]
        [InlineData("type")]
        [InlineData("source")]
        [InlineData("occurredAt")]
        [InlineData("receivedAt")]
        public async Task List_events_supports_allowed_sort_fields(string sortBy)
        {
            var now = DateTimeOffset.UtcNow;
            await InsertEventAsync(Guid.NewGuid(), "b.type", now.AddMinutes(-10), now.AddMinutes(-10), "{}", "{}");
            await InsertEventAsync(Guid.NewGuid(), "a.type", now.AddMinutes(-20), now.AddMinutes(-20), "{}", "{}");

            var response = await _client.GetAsync($"/v1/events?sortBy={sortBy}&sortDirection=asc&page=1&pageSize=5");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<PagedResponse<EventQueryDto>>();
            Assert.NotNull(body);
            Assert.Equal(2L, body!.TotalCount);
        }

        private async Task<Guid> SeedEventAsync()
        {
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@id, 'orders.created', @idem, @source, @now, @now, '{}'::jsonb, '{"orderId":"o-1"}'::jsonb);
                """,
                new
                {
                    id,
                    idem = "seed-" + id,
                    source = Guid.NewGuid(),
                    now
                });

            return id;
        }

        private async Task<Guid> InsertEventAsync(
            Guid sourceId,
            string type,
            DateTimeOffset occurredAt,
            DateTimeOffset receivedAt,
            string payload,
            string metadata)
        {
            var id = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@id, @type, @idem, @source, @occurredAt, @receivedAt, CAST(@metadata AS jsonb), CAST(@payload AS jsonb));
                """,
                new
                {
                    id,
                    type,
                    idem = "seed-" + id,
                    source = sourceId,
                    occurredAt,
                    receivedAt,
                    metadata,
                    payload
                });

            return id;
        }

        private async Task<string> InsertCustomerAsync(string id, DateTimeOffset createdAt)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO customers(id, display_name, created_at, updated_at)
                VALUES (@id, @displayName, @createdAt, @updatedAt);
                """,
                new
                {
                    id,
                    displayName = id,
                    createdAt,
                    updatedAt = createdAt
                });

            return id;
        }

        private async Task<Guid> InsertContractAsync(string customerId, DateTimeOffset createdAt)
        {
            var contractId = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO contracts(id, customer_id, event_type, destination_type, destination_config, is_active, created_at, updated_at)
                VALUES (@id, @customerId, 'orders.created', 'http', '{"url":"https://example.test/hook"}'::jsonb, TRUE, @createdAt, @updatedAt);
                """,
                new
                {
                    id = contractId,
                    customerId,
                    createdAt,
                    updatedAt = createdAt
                });

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
                INSERT INTO deliveries(
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
                    next_attempt_at,
                    created_at,
                    updated_at)
                VALUES (
                    @id,
                    @eventId,
                    @contractId,
                    @customerId,
                    'orders.created',
                    '{"orderId":"x"}'::jsonb,
                    'http',
                    '{"url":"https://example.test/hook"}'::jsonb,
                    @status,
                    @attemptCount,
                    @lastError,
                    @nextAttemptAt,
                    @createdAt,
                    @updatedAt);
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
                    updatedAt
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
                INSERT INTO delivery_attempts(
                    id,
                    delivery_id,
                    attempt_number,
                    succeeded,
                    failure_category,
                    error,
                    metadata,
                    started_at,
                    completed_at)
                VALUES (
                    @id,
                    @deliveryId,
                    @attemptNumber,
                    @succeeded,
                    @failureCategory,
                    @error,
                    '{}'::jsonb,
                    @startedAt,
                    @completedAt);
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
                    completedAt
                });
        }

        private sealed record EventQueryDto(
            Guid Id,
            string IdempotencyKey,
            string Type,
            string Source,
            DateTimeOffset OccurredAt,
            DateTimeOffset ReceivedAt,
            JsonElement Payload,
            JsonElement? Metadata,
            IReadOnlyList<DeliveryQueryDto> Deliveries);

        private sealed record DeliveryQueryDto(
            Guid Id,
            Guid EventId,
            string CustomerId,
            Guid ContractId,
            string EventType,
            string Status,
            int AttemptCount,
            string? LastError,
            DateTimeOffset? NextAttemptAt,
            DateTimeOffset CreatedAt,
            DateTimeOffset UpdatedAt,
            DeliveryAttemptSummaryDto AttemptSummary,
            DeliveryLatestFailureDto? LatestFailure);

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
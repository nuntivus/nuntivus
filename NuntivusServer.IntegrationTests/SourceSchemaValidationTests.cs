using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Controllers.Events;
using NuntivusServer.Contracts.Sources;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(EventsApiCollection))]
    public sealed class SourceSchemaValidationTests : IAsyncLifetime
    {
        private readonly EventsApiFixture _fixture;
        private readonly HttpClient _client;
        private readonly string _eventType = "schema." + Guid.NewGuid().ToString("N");
        private const string Schema = """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object",
            "required":["orderId"],"properties":{"orderId":{"type":"integer"}}}
            """;

        public SourceSchemaValidationTests(EventsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

        [Fact]
        public async Task No_active_schema_accepts_event_and_reports_not_configured()
        {
            var source = await CreateSourceAsync();
            var response = await PublishAsync(source, "no-schema", new { any = true });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var id = (await response.Content.ReadFromJsonAsync<PublishEventResponse>())!.Id;
            var detail = await _client.GetFromJsonAsync<EventQueryDto>($"/v1/events/{id}");
            Assert.Equal("NotConfigured", detail!.SchemaValidationStatus);
            Assert.Null(detail.InputSchemaVersion);
        }

        [Fact]
        public async Task Invalid_payload_returns_structured_errors_without_event_or_outbox_writes()
        {
            await CreateSchemaAsync(1, Schema);
            await ActivateAsync(1);
            var source = await CreateSourceAsync();

            var response = await PublishAsync(source, "invalid", new { orderId = "private-value" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Contains(error!.Errors, e => e.Path == "/payload/orderId" && e.Keyword == "type");
            Assert.DoesNotContain("private-value", await response.Content.ReadAsStringAsync());
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM events"));
            Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM outbox_messages"));
        }

        [Fact]
        public async Task Valid_payload_persists_version_and_replay_preserves_validation_metadata()
        {
            await CreateSchemaAsync(1, Schema);
            await ActivateAsync(1);
            var source = await CreateSourceAsync();
            var response = await PublishAsync(source, "valid", new { orderId = 42 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var id = (await response.Content.ReadFromJsonAsync<PublishEventResponse>())!.Id;
            var detail = await _client.GetFromJsonAsync<EventQueryDto>($"/v1/events/{id}");
            Assert.Equal("Valid", detail!.SchemaValidationStatus);
            Assert.Equal(1, detail.InputSchemaVersion);

            var replay = await _client.PostAsJsonAsync($"/v1/events/{id}/replay", new { replayKey = "schema-replay" });
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            var replayResult = (await replay.Content.ReadFromJsonAsync<ReplayEventResponse>())!;
            var replayed = await _client.GetFromJsonAsync<EventQueryDto>($"/v1/events/{replayResult.ReplayedEventId}");
            Assert.Equal("Valid", replayed!.SchemaValidationStatus);
            Assert.Equal(1, replayed.InputSchemaVersion);
        }

        [Fact]
        public async Task Active_schema_changes_are_immediate_and_duplicates_keep_original_version()
        {
            await CreateSchemaAsync(1, Schema);
            await ActivateAsync(1);
            var source = await CreateSourceAsync();
            var first = await PublishAsync(source, "duplicate", new { orderId = 1 });
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var originalId = (await first.Content.ReadFromJsonAsync<PublishEventResponse>())!.Id;

            await CreateSchemaAsync(2, """
                {"type":"object","required":["orderId"],"properties":{"orderId":{"type":"integer","minimum":10}}}
                """);
            await ActivateAsync(2);
            var duplicate = await PublishAsync(source, "duplicate", new { orderId = 1 });
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            Assert.Equal(originalId, (await duplicate.Content.ReadFromJsonAsync<PublishEventResponse>())!.Id);
            Assert.Equal(HttpStatusCode.BadRequest, (await PublishAsync(source, "too-small", new { orderId = 1 })).StatusCode);

            var next = await PublishAsync(source, "new", new { orderId = 20 });
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
            var newId = (await next.Content.ReadFromJsonAsync<PublishEventResponse>())!.Id;
            Assert.Equal(2, (await _client.GetFromJsonAsync<EventQueryDto>($"/v1/events/{newId}"))!.InputSchemaVersion);
            Assert.Equal(1, (await _client.GetFromJsonAsync<EventQueryDto>($"/v1/events/{originalId}"))!.InputSchemaVersion);

            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/v1/event-definitions/{_eventType}/schemas/2/deprecate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PublishAsync(source, "after-deprecation", new { any = true })).StatusCode);
        }

        [Theory]
        [InlineData("{\"type\":\"invalid-type\"}")]
        [InlineData("{\"$ref\":\"https://example.com/remote\"}")]
        [InlineData("{\"$ref\":\"#/$defs/missing\"}")]
        public async Task Schema_registration_rejects_invalid_or_unresolvable_documents(string schema)
        {
            await CreateDefinitionAsync();
            var response = await _client.PostAsJsonAsync($"/v1/event-definitions/{_eventType}/schemas",
                new { version = 1, schema, compatibilityMode = "Backward" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        private async Task CreateDefinitionAsync()
        {
            var response = await _client.PostAsJsonAsync("/v1/event-definitions", new { eventType = _eventType, displayName = "Schema test" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        private async Task CreateSchemaAsync(int version, string schema)
        {
            if (version == 1) await CreateDefinitionAsync();
            var response = await _client.PostAsJsonAsync($"/v1/event-definitions/{_eventType}/schemas",
                new { version, schema, compatibilityMode = "Backward" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        private async Task ActivateAsync(int version)
        {
            var response = await _client.PostAsJsonAsync($"/v1/event-definitions/{_eventType}/schemas/{version}/activate",
                new { overrideCompatibility = true, reason = "Explicit schema-validation regression scenario" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private async Task<SourceView> CreateSourceAsync()
        {
            var response = await _client.PostAsJsonAsync("/v1/sources", new
            {
                name = "Validated source", type = "HTTP_API", configuration = new { authentication = new { type = "none" } }
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<SourceView>())!;
        }

        private Task<HttpResponseMessage> PublishAsync(SourceView source, string key, object payload) =>
            _client.PostAsJsonAsync($"/v1/sources/{source.Id}/events", new
            {
                idempotencyKey = key, type = _eventType, timestamp = DateTimeOffset.UtcNow, payload
            });
    }
}

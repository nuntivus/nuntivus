using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Controllers.Events;
using NuntivusServer.Contracts.Events;
using Npgsql;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(EventDefinitionsApiCollection))]
    public sealed class EventDefinitionsApiTests : IAsyncLifetime
    {
        private readonly EventDefinitionsApiFixture _fixture;
        private readonly HttpClient _client;

        public EventDefinitionsApiTests(EventDefinitionsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Create_returns_201_and_persists_definition()
        {
            var resp = await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.created", "Order Created", "Raised when an order is created."));

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<EventDefinitionCreated>();
            Assert.NotNull(body);
            Assert.Equal("orders.created", body!.EventType);
            Assert.Equal("Order Created", body.DisplayName);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM event_definitions WHERE event_type = @eventType",
                new { eventType = "orders.created" });
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Create_with_invalid_event_type_returns_400_validation()
        {
            var resp = await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("BAD TYPE", "Order Created", null));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("validation", err!.Code);
        }

        [Fact]
        public async Task Create_with_duplicate_event_type_returns_409_conflict()
        {
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.updated", "Order Updated", null));

            var resp = await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.updated", "Order Updated Again", null));

            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("conflict", err!.Code);
        }

        [Fact]
        public async Task GetByEventType_returns_definition()
        {
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.fulfilled", "Order Fulfilled", "Ready for shipping."));

            var resp = await _client.GetAsync("/v1/event-definitions/orders.fulfilled");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<EventDefinitionView>();
            Assert.NotNull(body);
            Assert.Equal("orders.fulfilled", body!.EventType);
            Assert.Equal("Order Fulfilled", body.DisplayName);
            Assert.Equal("Ready for shipping.", body.Description);
        }

        [Fact]
        public async Task GetByEventType_missing_returns_404()
        {
            var resp = await _client.GetAsync("/v1/event-definitions/missing.type");

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("not_found", err!.Code);
        }

        [Fact]
        public async Task List_returns_paginated_results()
        {
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.created", "Order Created", null));
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.updated", "Order Updated", null));
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.cancelled", "Order Cancelled", null));

            var resp = await _client.GetAsync("/v1/event-definitions?page=2&pageSize=2");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<PagedResponse<EventDefinitionView>>();
            Assert.NotNull(body);
            Assert.Equal(2, body!.Page);
            Assert.Equal(2, body.PageSize);
            Assert.Equal(3, body.TotalCount);
            Assert.Equal(2, body.TotalPages);
            Assert.Single(body.Items);
            Assert.True(body.HasPrevious);
            Assert.False(body.HasNext);
        }

        [Theory]
        [InlineData(0, 20)]
        [InlineData(1, 0)]
        [InlineData(1, 101)]
        public async Task List_with_invalid_paging_returns_400(int page, int pageSize)
        {
            var resp = await _client.GetAsync($"/v1/event-definitions?page={page}&pageSize={pageSize}");
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_schema_draft_returns_201_and_persists_schema_version()
        {
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.created", "Order Created", null));

            var draftResp = await _client.PostAsJsonAsync(
                "/v1/event-definitions/orders.created/schemas",
                new CreateEventSchemaVersionRequest(
                    1,
                    "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}}",
                    SchemaCompatibilityMode.Backward));

            Assert.Equal(HttpStatusCode.Created, draftResp.StatusCode);
            var draft = await draftResp.Content.ReadFromJsonAsync<EventSchemaVersionView>();
            Assert.NotNull(draft);
            Assert.Equal(1, draft!.Version);
            Assert.Equal(EventSchemaState.Draft, draft.State);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM event_schema_versions WHERE event_type = @eventType AND version_number = @version",
                new { eventType = "orders.created", version = 1 });
            Assert.Equal(1, count);
        }

        [Theory]
        [InlineData("Backward")]
        [InlineData("Forward")]
        public async Task Create_schema_draft_accepts_string_compatibility_mode(string compatibilityMode)
        {
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.created", "Order Created", null));

            var resp = await _client.PostAsJsonAsync(
                "/v1/event-definitions/orders.created/schemas",
                new { version = 1, schema = "{}", compatibilityMode });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(compatibilityMode, body.GetProperty("compatibilityMode").GetString());
            Assert.Equal("Draft", body.GetProperty("state").GetString());
        }

        [Fact]
        public async Task Activate_schema_version_enforces_single_active_schema()
        {
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.updated", "Order Updated", null));

            await _client.PostAsJsonAsync(
                "/v1/event-definitions/orders.updated/schemas",
                new CreateEventSchemaVersionRequest(1, "{}", SchemaCompatibilityMode.Backward));
            await _client.PostAsJsonAsync(
                "/v1/event-definitions/orders.updated/schemas",
                new CreateEventSchemaVersionRequest(2, "{}", SchemaCompatibilityMode.Forward));

            var activateV1 = await _client.PostAsync("/v1/event-definitions/orders.updated/schemas/1/activate", content: null);
            Assert.Equal(HttpStatusCode.OK, activateV1.StatusCode);

            var activateV2 = await _client.PostAsync("/v1/event-definitions/orders.updated/schemas/2/activate", content: null);
            Assert.Equal(HttpStatusCode.OK, activateV2.StatusCode);

            var v1Resp = await _client.GetAsync("/v1/event-definitions/orders.updated/schemas/1");
            var v1 = await v1Resp.Content.ReadFromJsonAsync<EventSchemaVersionView>();
            Assert.Equal(EventSchemaState.Draft, v1!.State);

            var v2Resp = await _client.GetAsync("/v1/event-definitions/orders.updated/schemas/2");
            var v2 = await v2Resp.Content.ReadFromJsonAsync<EventSchemaVersionView>();
            Assert.Equal(EventSchemaState.Active, v2!.State);
        }

        [Fact]
        public async Task Deprecate_schema_version_returns_deprecated_state()
        {
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.cancelled", "Order Cancelled", null));

            await _client.PostAsJsonAsync(
                "/v1/event-definitions/orders.cancelled/schemas",
                new CreateEventSchemaVersionRequest(1, "{}", SchemaCompatibilityMode.Backward));

            var deprecateResp = await _client.PostAsync("/v1/event-definitions/orders.cancelled/schemas/1/deprecate", content: null);
            Assert.Equal(HttpStatusCode.OK, deprecateResp.StatusCode);

            var body = await deprecateResp.Content.ReadFromJsonAsync<EventSchemaVersionView>();
            Assert.NotNull(body);
            Assert.Equal(EventSchemaState.Deprecated, body!.State);
        }

        [Fact]
        public async Task Create_schema_draft_with_invalid_json_returns_400()
        {
            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.failed", "Order Failed", null));

            var resp = await _client.PostAsJsonAsync(
                "/v1/event-definitions/orders.failed/schemas",
                new CreateEventSchemaVersionRequest(1, "not-json", SchemaCompatibilityMode.Backward));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
    }
}
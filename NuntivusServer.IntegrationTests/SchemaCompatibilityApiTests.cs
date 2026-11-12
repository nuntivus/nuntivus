using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using NuntivusServer.Contracts.Events;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(EventsApiCollection))]
    public sealed class SchemaCompatibilityApiTests : IAsyncLifetime
    {
        private readonly EventsApiFixture _fixture;
        private readonly HttpClient _client;
        private readonly string _type = "compat." + Guid.NewGuid().ToString("N");
        private const string Baseline = """{"type":"object","properties":{"id":{"type":"integer"}}}""";
        private const string Breaking = """{"type":"object","required":["id"],"properties":{"id":{"type":"integer"}}}""";
        public SchemaCompatibilityApiTests(EventsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
        }
        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

        [Fact]
        public async Task Breaking_activation_is_blocked_without_changing_active_version()
        {
            await SetupAsync(Breaking);
            var report = await _client.GetFromJsonAsync<SchemaCompatibilityReport>($"{Root}/2/compatibility");
            Assert.Equal(SchemaCompatibilityStatus.Breaking, report!.Status);
            Assert.Contains(report.Findings, finding => finding.Keyword == "required");
            Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"{Root}/2/activate", null)).StatusCode);
            Assert.Equal(EventSchemaState.Active, (await _client.GetFromJsonAsync<EventSchemaVersionView>($"{Root}/1"))!.State);
            Assert.Equal(EventSchemaState.Draft, (await _client.GetFromJsonAsync<EventSchemaVersionView>($"{Root}/2"))!.State);
        }

        [Fact]
        public async Task Indeterminate_changes_are_not_implicitly_activated()
        {
            await SetupAsync("""{"allOf":[{"type":"object"}]}""");
            Assert.Equal(SchemaCompatibilityStatus.Indeterminate,
                (await _client.GetFromJsonAsync<SchemaCompatibilityReport>($"{Root}/2/compatibility"))!.Status);
            Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"{Root}/2/activate", null)).StatusCode);
        }

        [Fact]
        public async Task Operator_cannot_override_even_when_body_claims_an_admin_actor()
        {
            await SetupAsync(Breaking);
            var response = await _client.PostAsJsonAsync($"{Root}/2/activate", new
            {
                overrideCompatibility = true, reason = "Operator requested override", actorId = "fake-admin", canOverride = true
            });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(EventSchemaState.Active, (await _client.GetFromJsonAsync<EventSchemaVersionView>($"{Root}/1"))!.State);
        }

        [Fact]
        public async Task Admin_override_requires_reason_and_audits_authenticated_identity_and_full_snapshot()
        {
            await SetupAsync(Breaking);
            _client.DefaultRequestHeaders.Remove("X-Test-Role");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
            _client.DefaultRequestHeaders.Add("X-Operator-Id", "schema-admin");
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"{Root}/2/activate", new SchemaActivationOptions(true))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"{Root}/2/activate", new SchemaActivationOptions(true, "Reviewed required-field change"))).StatusCode);
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            var actor = await connection.ExecuteScalarAsync<string>("SELECT actor_id FROM audit_logs WHERE category = 'event-schema' AND action = 'activation-overridden'");
            Assert.Equal("schema-admin", actor);
            var details = await connection.ExecuteScalarAsync<string>("SELECT details::text FROM audit_logs WHERE category = 'event-schema' AND action = 'activation-overridden'");
            using var snapshot = JsonDocument.Parse(details!);
            Assert.Equal("Reviewed required-field change", snapshot.RootElement.GetProperty("request").GetProperty("reason").GetString());
            Assert.Equal("Breaking", snapshot.RootElement.GetProperty("compatibility").GetProperty("Status").GetString());
            Assert.Equal(2, snapshot.RootElement.GetProperty("response").GetProperty("Version").GetInt32());
            Assert.True(snapshot.RootElement.GetProperty("response").TryGetProperty("Schema", out _));
        }

        [Fact]
        public async Task Concurrent_overrides_compare_against_the_serialized_current_baseline()
        {
            await SetupAsync(Breaking);
            await AddSchemaAsync(3, """{"type":"object","required":["id","name"]}""");
            _client.DefaultRequestHeaders.Remove("X-Test-Role");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
            var responses = await Task.WhenAll(_client.PostAsJsonAsync($"{Root}/2/activate", new SchemaActivationOptions(true, "Reviewed v2")),
                _client.PostAsJsonAsync($"{Root}/3/activate", new SchemaActivationOptions(true, "Reviewed v3")));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            var snapshots = (await connection.QueryAsync<string>("SELECT details::text FROM audit_logs WHERE action = 'activation-overridden' ORDER BY created_at, id")).Select(text => JsonDocument.Parse(text)).ToArray();
            try
            {
                Assert.Equal(2, snapshots.Length);
                var firstVersion = snapshots[0].RootElement.GetProperty("response").GetProperty("Version").GetInt32();
                Assert.Equal(firstVersion, snapshots[1].RootElement.GetProperty("compatibility").GetProperty("AgainstVersion").GetInt32());
                Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM event_schema_versions WHERE event_type = @Type AND state_code = 'Active'", new { Type = _type }));
            }
            finally { foreach (var snapshot in snapshots) snapshot.Dispose(); }
        }

        [Fact]
        public async Task Forward_mode_allows_new_payloads_that_old_schema_can_accept()
        {
            await SetupAsync(Breaking, "Forward");
            Assert.Equal(SchemaCompatibilityStatus.Compatible,
                (await _client.GetFromJsonAsync<SchemaCompatibilityReport>($"{Root}/2/compatibility"))!.Status);
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"{Root}/2/activate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Root}/2/compatibility?againstVersion=999")).StatusCode);
        }

        private string Root => $"/v1/event-definitions/{_type}/schemas";
        private async Task SetupAsync(string proposed, string mode = "Backward")
        {
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/v1/event-definitions", new { eventType = _type, displayName = "Compatibility test" })).StatusCode);
            await AddSchemaAsync(1, Baseline);
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"{Root}/1/activate", null)).StatusCode);
            await AddSchemaAsync(2, proposed, mode);
        }
        private async Task AddSchemaAsync(int version, string schema, string mode = "Backward") =>
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync(Root, new { version, schema, compatibilityMode = mode })).StatusCode);
    }
}

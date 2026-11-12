using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using NuntivusServer.Api.Controllers.Events;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Contracts.Destinations;
using NuntivusServer.Contracts.Events;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(ContractsApiCollection))]
    public sealed class ChangeImpactApiTests : IAsyncLifetime
    {
        private readonly ContractsApiFixture _fixture;
        private readonly HttpClient _client;
        public ChangeImpactApiTests(ContractsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
        }
        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

        [Fact]
        public async Task Schema_impact_lists_affected_contract_bindings_without_mutation()
        {
            var contract = await SetupAsync();
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            var before = await SnapshotAsync(connection);
            var response = await _client.GetAsync("/v1/event-definitions/orders.created/schemas/2/impact?sampleSize=0");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var report = (await response.Content.ReadFromJsonAsync<ChangeImpactReport>())!;
            Assert.Equal(1, report.TotalContracts);
            var affected = Assert.Single(report.Contracts);
            Assert.Equal(contract.Id, affected.ContractId);
            Assert.Equal("impact-customer", affected.CustomerId);
            Assert.Equal("Breaking", affected.Status);
            Assert.NotEmpty(affected.Bindings);
            Assert.Contains("not proof", report.EvidenceNotice);
            Assert.Equal(before, await SnapshotAsync(connection));
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/v1/event-definitions/orders.created/schemas/2/impact?sampleSize=21")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/v1/event-definitions/orders.created/schemas/999/impact")).StatusCode);
        }

        [Fact]
        public async Task Schema_impact_uses_candidate_mode_and_preserves_breaking_with_transformation()
        {
            var contract = await SetupAsync();
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/v1/event-definitions/orders.created/schemas",
                new CreateEventSchemaVersionRequest(3, """{"type":"integer"}""", SchemaCompatibilityMode.Forward))).StatusCode);
            var forward = await _client.GetFromJsonAsync<ChangeImpactReport>("/v1/event-definitions/orders.created/schemas/3/impact?sampleSize=0");
            Assert.Equal("Compatible", Assert.Single(forward!.Contracts).Status);
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.ExecuteAsync("UPDATE contracts SET transformation_config = CAST(@Transformation AS jsonb) WHERE id = @Id",
                new { Id = contract.Id, Transformation = """{"version":2,"mappings":[],"expression":{"op":"constant","value":{}}}""" });
            var breaking = await _client.GetFromJsonAsync<ChangeImpactReport>("/v1/event-definitions/orders.created/schemas/2/impact?sampleSize=0");
            Assert.Equal("Breaking", Assert.Single(breaking!.Contracts).Status);
        }

        [Fact]
        public async Task Transformation_impact_is_read_only_and_does_not_claim_sample_proof()
        {
            var contract = await SetupAsync();
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            var before = await SnapshotAsync(connection);
            using var expression = JsonDocument.Parse("""{"op":"constant","value":{"id":42}}""");
            var request = new TransformationImpactRequest("orders.created", new ContractPayloadTransformationRequest(2, [], expression.RootElement), 0);
            var response = await _client.PostAsJsonAsync($"/v1/contracts/{contract.Id}/transformation-impact", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var report = (await response.Content.ReadFromJsonAsync<ChangeImpactReport>())!;
            Assert.Equal("ReviewRequired", Assert.Single(report.Contracts).Status);
            Assert.NotNull(report.ProposedTransformationIdentity);
            Assert.Equal(before, await SnapshotAsync(connection));
            var unchanged = await _client.PostAsJsonAsync($"/v1/contracts/{contract.Id}/transformation-impact", new TransformationImpactRequest("orders.created", null, 0));
            Assert.Equal("Compatible", Assert.Single((await unchanged.Content.ReadFromJsonAsync<ChangeImpactReport>())!.Contracts).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/v1/contracts/{contract.Id}/transformation-impact", request with { EventType = "orders.other" })).StatusCode);
        }

        private async Task<ContractCreated> SetupAsync()
        {
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/v1/customers", new { id = "impact-customer", name = "Impact" })).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/v1/event-definitions", new { eventType = "orders.created", displayName = "Orders" })).StatusCode);
            foreach (var version in new[] { 1, 2 })
                Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/v1/event-definitions/orders.created/schemas",
                    new CreateEventSchemaVersionRequest(version, version == 1 ? "{}" : """{"type":"object","required":["id"]}""", SchemaCompatibilityMode.Backward))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/v1/event-definitions/orders.created/schemas/1/activate", null)).StatusCode);
            var created = await _client.PostAsJsonAsync("/v1/contracts", new CreateContractRequest("impact-customer", ["orders.created"],
                new HttpDestinationConfig("https://hooks.example.com/impact", new HttpAuthentication("none")), SourceIds: [ContractsApiFixture.SourceId]));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return (await created.Content.ReadFromJsonAsync<ContractCreated>())!;
        }

        private static Task<string?> SnapshotAsync(NpgsqlConnection connection) => connection.ExecuteScalarAsync<string>("""
            SELECT jsonb_build_object('events', (SELECT count(*) FROM events), 'deliveries', (SELECT count(*) FROM deliveries),
                'outbox', (SELECT count(*) FROM outbox_messages), 'audit', (SELECT count(*) FROM audit_logs),
                'contracts', (SELECT jsonb_agg(to_jsonb(c)) FROM contracts c),
                'schemas', (SELECT jsonb_agg(to_jsonb(s)) FROM event_schema_versions s))::text
            """);
    }
}

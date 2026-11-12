using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Contracts.Destinations;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(ContractsApiCollection))]
    public sealed class ContractDryRunApiTests : IAsyncLifetime
    {
        private readonly ContractsApiFixture _fixture;
        private readonly HttpClient _client;
        public ContractDryRunApiTests(ContractsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
        }
        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

        [Fact]
        public async Task Unsaved_draft_runs_full_pipeline_without_persistence_or_network()
        {
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/v1/customers", new { id = "dry-customer", name = "Dry" })).StatusCode);
            using var expression = JsonDocument.Parse("""{"op":"object","fields":{"outputId":{"op":"select","path":"/id"}}}""");
            var draft = new CreateContractRequest("dry-customer", ["orders.created"],
                new HttpDestinationConfig("https://does-not-exist.example.com/dry-run", new HttpAuthentication("none")),
                Transformation: new ContractPayloadTransformationRequest(2, [], expression.RootElement), SourceIds: [ContractsApiFixture.SourceId]);
            var input = new ContractDryRunRequest("orders.created", ContractsApiFixture.SourceId,
                JsonSerializer.SerializeToElement(new { id = 42 }), Draft: draft);
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            var before = await SnapshotAsync(connection);
            var response = await _client.PostAsJsonAsync("/v1/contracts/dry-run", input);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var report = (await response.Content.ReadFromJsonAsync<ContractDryRunReport>())!;
            Assert.True(report.WouldDeliver);
            Assert.Equal(42, report.TransformedPayload!.Value.GetProperty("outputId").GetInt32());
            Assert.Contains(report.Stages, stage => stage.Stage == "inputSchema" && stage.Status == "Passed");
            Assert.Contains(report.Stages, stage => stage.Stage == "runtimeControls" && stage.Status == "NotSimulated");
            Assert.Equal("NotConnected", Assert.Single(report.Destinations).Status);
            Assert.Equal(before, await SnapshotAsync(connection));
            var unmatched = await _client.PostAsJsonAsync("/v1/contracts/dry-run", input with { DraftEnabled = false });
            Assert.False((await unmatched.Content.ReadFromJsonAsync<ContractDryRunReport>())!.WouldDeliver);
        }

        [Fact]
        public async Task Saved_contract_dry_run_reports_filter_failure_without_writes()
        {
            await _client.PostAsJsonAsync("/v1/customers", new { id = "dry-saved", name = "Dry" });
            var createdResponse = await _client.PostAsJsonAsync("/v1/contracts", new CreateContractRequest("dry-saved", ["orders.created"],
                new HttpDestinationConfig("https://hooks.example.com/dry", new HttpAuthentication("none")),
                Filter: new ContractFilterRequest(PayloadRules: [new ContractFilterRuleRequest("id", ContractFilterOperator.Equals, "99")]),
                SourceIds: [ContractsApiFixture.SourceId]));
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            var created = (await createdResponse.Content.ReadFromJsonAsync<ContractCreated>())!;
            var input = new ContractDryRunRequest("orders.created", ContractsApiFixture.SourceId, JsonSerializer.SerializeToElement(new { id = 42 }));
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            var before = await SnapshotAsync(connection);
            var response = await _client.PostAsJsonAsync($"/v1/contracts/{created.Id}/dry-run", input);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var report = (await response.Content.ReadFromJsonAsync<ContractDryRunReport>())!;
            Assert.False(report.WouldDeliver);
            Assert.Contains(report.Stages, stage => stage.Stage == "filter" && stage.Status == "Failed");
            Assert.Equal(before, await SnapshotAsync(connection));
        }

        private static Task<string?> SnapshotAsync(NpgsqlConnection connection) => connection.ExecuteScalarAsync<string>("""
            SELECT jsonb_build_object('events', (SELECT count(*) FROM events), 'deliveries', (SELECT count(*) FROM deliveries),
                'outbox', (SELECT count(*) FROM outbox_messages), 'audit', (SELECT count(*) FROM audit_logs),
                'contracts', (SELECT jsonb_agg(to_jsonb(c)) FROM contracts c))::text
            """);
    }
}

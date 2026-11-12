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
    public sealed class ContractSloApiTests(ContractsApiFixture fixture) : IAsyncLifetime
    {
        private HttpClient _client = null!;
        public async Task InitializeAsync()
        {
            await fixture.ResetAsync();
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
        }
        public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

        [Fact]
        public async Task Cohorts_count_logical_destinations_exclude_tests_and_preserve_retained_facts()
        {
            var id = await CreateAsync();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("UPDATE contract_slo_policies SET monitoring_started_at = now() - interval '40 days' WHERE contract_id = @Id", new { Id = id });
            var good = await SeedAsync(connection, id, "{}", "Delivered", 10);
            await SeedAsync(connection, id, "{}", "Delivered", 90);
            await SeedAsync(connection, id, "{}", "Cancelled", null);
            await SeedAsync(connection, id, "{\"nuntivus.test\":\"true\"}", "Delivered", 1);
            await SeedAsync(connection, id, "{\"nuntivus.replay.sourceEventId\":\"original\"}", "Delivered", 1);
            var report = (await _client.GetFromJsonAsync<ContractSloReport>($"/v1/contracts/{id}/slo"))!;
            Assert.True(report.FullWindowCovered);
            Assert.Equal(3, report.Success.Eligible);
            Assert.Equal(2, report.Success.Good);
            Assert.Equal(1, report.Latency.Good);
            Assert.Equal(2, report.Excluded);
            await connection.ExecuteAsync("DELETE FROM deliveries WHERE id = @Id", new { Id = good });
            var retained = (await _client.GetFromJsonAsync<ContractSloReport>($"/v1/contracts/{id}/slo"))!;
            Assert.Equal(report.Success, retained.Success);
            Assert.Equal(report.Latency, retained.Latency);
        }

        [Fact]
        public async Task Retry_updates_one_observation_and_late_success_does_not_repair_deadline()
        {
            var id = await CreateAsync();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("UPDATE contract_slo_policies SET monitoring_started_at = now() - interval '40 days' WHERE contract_id = @Id", new { Id = id });
            var delivery = await SeedAsync(connection, id, "{}", "Failed", null);
            await connection.ExecuteAsync("UPDATE deliveries SET status = 'Delivered', updated_at = created_at + interval '25 hours' WHERE id = @Id", new { Id = delivery });
            var report = (await _client.GetFromJsonAsync<ContractSloReport>($"/v1/contracts/{id}/slo"))!;
            Assert.Equal(1, report.Success.Eligible);
            Assert.Equal(0, report.Success.Good);
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM delivery_slo_observations WHERE delivery_id = @Id", new { Id = delivery }));
            await connection.ExecuteAsync("UPDATE deliveries SET status = 'Failed' WHERE id = @Id; UPDATE deliveries SET status = 'Delivered', updated_at = created_at + interval '1 second' WHERE id = @Id", new { Id = delivery });
            Assert.Equal(0, (await _client.GetFromJsonAsync<ContractSloReport>($"/v1/contracts/{id}/slo"))!.Success.Good);
        }

        [Fact]
        public async Task Duplicate_policy_preserves_monitoring_and_audit_uses_principal()
        {
            var id = await CreateAsync();
            var first = (await _client.GetFromJsonAsync<ContractSloReport>($"/v1/contracts/{id}/slo"))!;
            var response = await _client.PutAsJsonAsync($"/v1/contracts/{id}/slo", new ContractSloPolicyRequest(99, 95, 60));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var duplicate = (await response.Content.ReadFromJsonAsync<ContractSloReport>())!;
            Assert.Equal(first.MonitoringStartedAt, duplicate.MonitoringStartedAt);
            Assert.Null(duplicate.Success.AttainmentPercent);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE contract_id = @Id AND action = 'slo_configured'", new { Id = id }));
            Assert.Equal("operator-it", await connection.ExecuteScalarAsync<string>("SELECT actor_id FROM audit_logs WHERE contract_id = @Id AND action = 'slo_configured'", new { Id = id }));
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/v1/contracts/{id}/slo", new ContractSloPolicyRequest(101, 95, 60))).StatusCode);
        }

        [Fact]
        public async Task Pending_cohorts_and_expired_observation_cleanup_do_not_claim_success()
        {
            var id = await CreateAsync();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("UPDATE contract_slo_policies SET monitoring_started_at = now() - interval '40 days' WHERE contract_id = @Id", new { Id = id });
            var delivery = await SeedAsync(connection, id, "{}", "Pending", null);
            await connection.ExecuteAsync("UPDATE delivery_slo_observations SET created_at = now() WHERE delivery_id = @Id", new { Id = delivery });
            var report = (await _client.GetFromJsonAsync<ContractSloReport>($"/v1/contracts/{id}/slo"))!;
            Assert.Equal(1, report.Success.Pending);
            Assert.Equal(0, report.Success.Eligible);
            Assert.Null(report.Success.AttainmentPercent);
            var expired = await SeedAsync(connection, id, "{}", "Dead", null);
            await connection.ExecuteAsync("UPDATE delivery_slo_observations SET created_at = now() - interval '46 days' WHERE delivery_id = @Id", new { Id = expired });
            var retention = new NuntivusServer.Infastructure.Persistence.Postgres.PostgresSloObservationRetention(
                new NuntivusServer.Infastructure.Persistence.Postgres.PostgresDatabaseConnectionFactory(fixture.ConnectionString));
            (await retention.DeleteExpiredBatchAsync(CancellationToken.None)).EnsureSuccess();
            Assert.False(await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM delivery_slo_observations WHERE delivery_id = @Id)", new { Id = expired }));
            Assert.True(await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM delivery_slo_observations WHERE delivery_id = @Id)", new { Id = delivery }));
            Assert.Equal(report.Success, (await _client.GetFromJsonAsync<ContractSloReport>($"/v1/contracts/{id}/slo"))!.Success);
        }

        [Fact]
        public async Task Changed_policy_restarts_coverage_and_viewer_cannot_configure()
        {
            var id = await CreateAsync();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("UPDATE contract_slo_policies SET monitoring_started_at = now() - interval '40 days' WHERE contract_id = @Id", new { Id = id });
            var changed = await _client.PutAsJsonAsync($"/v1/contracts/{id}/slo", new ContractSloPolicyRequest(98, 95, 60));
            Assert.False((await changed.Content.ReadFromJsonAsync<ContractSloReport>())!.FullWindowCovered);
            _client.DefaultRequestHeaders.Remove("X-Test-Role");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/v1/contracts/{id}/slo")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.PutAsJsonAsync($"/v1/contracts/{id}/slo", new ContractSloPolicyRequest(97, 95, 60))).StatusCode);
        }

        private async Task<Guid> CreateAsync()
        {
            await _client.PostAsJsonAsync("/v1/customers", new { id = "slo-customer", name = "SLO" });
            var response = await _client.PostAsJsonAsync("/v1/contracts", new CreateContractRequest("slo-customer", ["orders.created"],
                new HttpDestinationConfig("https://hooks.example.com/slo", new HttpAuthentication("none")), SourceIds: [ContractsApiFixture.SourceId]));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var id = (await response.Content.ReadFromJsonAsync<ContractCreated>())!.Id;
            var configured = await _client.PutAsJsonAsync($"/v1/contracts/{id}/slo", new ContractSloPolicyRequest(99, 95, 60));
            Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
            return id;
        }

        private static async Task<Guid> SeedAsync(NpgsqlConnection connection, Guid contractId, string metadata, string status, int? seconds)
        {
            var id = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            await connection.ExecuteAsync("""
                INSERT INTO events (id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@EventId, 'orders.created', @Key, @Source, now() - interval '2 days', now() - interval '2 days', @Metadata::jsonb, '{}');
                INSERT INTO deliveries (id, event_id, contract_id, destination_binding_id, customer_id, event_type, payload,
                    destination_type, destination_config, status, attempt_count, created_at, updated_at)
                SELECT @Id, @EventId, c.id, b.id, c.customer_id, 'orders.created', '{}', 'Http', c.destination_config, @Status, 0,
                    now() - interval '2 days', now() - interval '2 days' + make_interval(secs => COALESCE(@Seconds, 0))
                FROM contracts c JOIN contract_destinations b ON b.contract_id = c.id WHERE c.id = @ContractId LIMIT 1;
                """, new { Id = id, EventId = eventId, Key = id.ToString(), Source = ContractsApiFixture.SourceId, ContractId = contractId, Metadata = metadata, Status = status, Seconds = seconds });
            return id;
        }
    }
}

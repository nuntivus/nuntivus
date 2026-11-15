using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using NuntivusServer.Application.Exports;
using NuntivusServer.Infastructure.Persistence.Postgres;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(ContractsApiCollection))]
    public sealed class DataExportApiTests(ContractsApiFixture fixture) : IAsyncLifetime
    {
        private HttpClient _client = null!;
        public async Task InitializeAsync()
        {
            await fixture.ResetAsync();
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("""
                INSERT INTO customers (id, name, created_at, updated_at) VALUES ('=FORMULA()', 'Export', now(), now());
                INSERT INTO contracts (id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@Contract, '=FORMULA()', true, 'Http', '{"password":"do-not-export"}', now(), now());
                INSERT INTO events (id, type, idempotency_key, source_id, occurred_at, received_at, payload, metadata)
                VALUES (@Event, 'orders.created', 'sensitive-key', @Source, now(), now(), '{"password":"private-payload"}', '{"secret":"private-metadata"}');
                INSERT INTO deliveries (id, event_id, contract_id, customer_id, event_type, payload, destination_type, destination_config,
                    status, attempt_count, last_error, created_at, updated_at)
                VALUES (@Delivery, @Event, @Contract, '=FORMULA()', 'orders.created', '{}', 'Http', '{}', 'Failed', 1, 'private-error', now(), now());
                """, new { Contract, Event, Delivery, Source = ContractsApiFixture.SourceId });
        }
        private static readonly Guid Contract = Guid.Parse("d129c275-f519-4498-9d75-af9cc7c6ba68");
        private static readonly Guid Event = Guid.Parse("5cbdc0ae-a67c-4f69-bf87-c7d39ecb9dd7");
        private static readonly Guid Delivery = Guid.Parse("839284cb-a0d8-4585-a450-624457cc34cc");
        public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

        [Theory]
        [InlineData("events", "type=orders.created", "private-payload")]
        [InlineData("deliveries", "status=Failed", "private-error")]
        [InlineData("contracts", "enabled=true", "do-not-export")]
        public async Task Json_exports_filter_metadata_and_audit_principal(string entity, string filter, string excluded)
        {
            var response = await _client.GetAsync($"/v1/exports/{entity}?format=json&{filter}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Content.Headers.ContentDisposition!.DispositionType == "attachment");
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            Assert.Equal(1, json.RootElement.GetProperty("count").GetInt32());
            Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.DoesNotContain(excluded, body);
            Assert.DoesNotContain("private-metadata", body);
            Assert.DoesNotContain("sensitive-key", body);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE category = 'export' AND actor_id = 'operator-it'"));
        }
        [Fact]
        public async Task Csv_escapes_formula_cells_and_empty_filters_return_header()
        {
            var csv = await _client.GetStringAsync("/v1/exports/contracts?format=csv");
            Assert.Contains("\"'=FORMULA()\"", csv);
            var empty = await _client.GetStringAsync("/v1/exports/events?format=csv&type=missing");
            Assert.Single(empty.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        [Theory]
        [InlineData("", 2)]
        [InlineData("&status=Failed", 1)]
        [InlineData("&status=Dead", 1)]
        [InlineData("&status=Delivered", 0)]
        public async Task Failure_exports_include_both_failure_states_and_respect_status_filters(string statusFilter, int expectedCount)
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            foreach (var status in new[] { "Dead", "Pending", "InFlight", "Delivered" })
            {
                await connection.ExecuteAsync("""
                    INSERT INTO events (id, type, idempotency_key, source_id, occurred_at, received_at, payload)
                    VALUES (@Id, 'orders.created', @Key, @Source, now(), now(), '{}');
                    INSERT INTO deliveries (id, event_id, contract_id, customer_id, event_type, payload, destination_type, destination_config,
                        status, attempt_count, created_at, updated_at)
                    VALUES (@Id, @Id, @Contract, '=FORMULA()', 'orders.created', '{}', 'Http', '{}', @Status, 0, now(), now());
                    """, new { Id = Guid.NewGuid(), Key = Guid.NewGuid().ToString("N"), Source = ContractsApiFixture.SourceId, Contract, Status = status });
            }

            var response = await _client.GetAsync($"/v1/exports/deliveries?format=json&failuresOnly=true&contractId={Contract}{statusFilter}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expectedCount, json.RootElement.GetProperty("count").GetInt32());
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(expectedCount, items.Length);
            Assert.All(items, item => Assert.Contains(item.GetProperty("status").GetString(), new[] { "Failed", "Dead" }));
        }
        [Fact]
        public async Task Viewer_and_anonymous_cannot_export_and_invalid_options_are_rejected()
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/v1/exports/events?format=xml")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/v1/exports/unknown")).StatusCode);
            _client.DefaultRequestHeaders.Remove("X-Test-Role");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/v1/exports/events")).StatusCode);
            _client.DefaultRequestHeaders.Remove("X-Test-Auth");
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/v1/exports/events")).StatusCode);
        }
        [Fact]
        public async Task Snapshot_remains_consistent_after_updates_and_inserts()
        {
            var reader = new PostgresDataExportReader(new PostgresDatabaseConnectionFactory(fixture.ConnectionString));
            await using var session = (await reader.OpenAsync("deliveries", new DataExportRequest(), CancellationToken.None)).EnsureValue();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("""
                UPDATE deliveries SET status = 'Delivered' WHERE id = @Id;
                INSERT INTO events (id, type, idempotency_key, source_id, occurred_at, received_at, payload)
                VALUES (@New, 'new', 'snapshot-new', @Source, now(), now(), '{}');
                INSERT INTO deliveries (id, event_id, contract_id, customer_id, event_type, payload, destination_type, destination_config,
                    status, attempt_count, created_at, updated_at)
                VALUES (@New, @New, @Contract, '=FORMULA()', 'new', '{}', 'Http', '{}', 'Pending', 0, now(), now());
                """, new { Id = Delivery, New = Guid.NewGuid(), Contract, Source = ContractsApiFixture.SourceId });
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            await foreach (var row in session.ReadAsync(CancellationToken.None)) rows.Add(row);
            Assert.Single(rows);
            Assert.Equal("Failed", rows[0]["status"]);
        }
        [Fact]
        public async Task Cancelled_reader_releases_snapshot_and_connection()
        {
            var reader = new PostgresDataExportReader(new PostgresDatabaseConnectionFactory(fixture.ConnectionString));
            await using (var session = (await reader.OpenAsync("events", new DataExportRequest(), CancellationToken.None)).EnsureValue())
            {
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                {
                    await foreach (var row in session.ReadAsync(cancelled.Token)) cancelled.Token.ThrowIfCancellationRequested();
                });
            }
            await using var reopened = (await reader.OpenAsync("events", new DataExportRequest(), CancellationToken.None)).EnsureValue();
            Assert.Equal(1, reopened.Count);
        }
        [Fact]
        public async Task Oversized_export_is_rejected_before_streaming()
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("""
                INSERT INTO events (id, type, idempotency_key, source_id, occurred_at, received_at, payload)
                SELECT gen_random_uuid(), 'bulk', n::text, @Source, now(), now(), '{}' FROM generate_series(1, 10001) n
                """, new { Source = ContractsApiFixture.SourceId });
            var response = await _client.GetAsync("/v1/exports/events?type=bulk");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("10000", await response.Content.ReadAsStringAsync());
            Assert.Null(response.Content.Headers.ContentDisposition);
        }
    }
}

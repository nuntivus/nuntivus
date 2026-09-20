using System.IO;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KairoServer.IntegrationTests
{
    /// <summary>
    /// Fixture for Contracts API tests. Spins up Postgres, applies the customers
    /// and contracts migrations, and hosts the API via WebApplicationFactory.
    /// </summary>
    public sealed class ContractsApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("kairo_it")
            .WithUsername("kairo")
            .WithPassword("kairo")
            .Build();

        public string ConnectionString => _postgres.GetConnectionString();

        public async Task InitializeAsync()
        {
            await _postgres.StartAsync();
            await ApplyMigrationsAsync();
        }

        public new async Task DisposeAsync()
        {
            await _postgres.DisposeAsync();
            await base.DisposeAsync();
        }

        public async Task ResetAsync()
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            // contract_event_types cascades from contracts; contracts cascades from customers.
            await conn.ExecuteAsync("TRUNCATE TABLE customers CASCADE;");
            await conn.ExecuteAsync("TRUNCATE TABLE event_schema_versions, event_definitions;");
            await conn.ExecuteAsync("TRUNCATE TABLE audit_logs;");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = ConnectionString,
                    ["Database:PostgresConnectionString"] = ConnectionString
                });
            });
        }

        private async Task ApplyMigrationsAsync()
        {
            var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");

            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            foreach (var file in new[]
            {
                "001_create_events.sql",
                "002_create_outbox_messages.sql",
                "003_create_customers.sql",
                "004_create_contracts.sql",
                "005_create_deliveries.sql",
                "006_create_delivery_attempts.sql",
                "007_create_event_replays.sql",
                "008_create_event_definitions.sql",
                "009_create_contract_filters.sql",
                "010_add_contract_payload_transformations.sql",
                "011_add_delivery_rate_limits.sql",
                "012_add_delivery_ordering.sql",
                "013_add_retention_cleanup_indexes.sql",
                "014_add_outbox_retry_metadata.sql",
                "015_add_outbox_claim_indexes.sql",
                "016_add_delivery_contract_snapshots.sql",
                "017_add_contract_snapshot_columns.sql",
                "018_add_delivery_scheduler_indexes.sql",
                "019_add_delivery_operation_audits.sql",
                "020_create_delivery_replay_jobs.sql",
                "021_create_audit_logs.sql",
                "022_add_contract_name.sql"
            })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(migrationsDir, file));
                await conn.ExecuteAsync(sql);
            }
        }
    }

    [CollectionDefinition(nameof(ContractsApiCollection))]
    public sealed class ContractsApiCollection : ICollectionFixture<ContractsApiFixture> { }
}

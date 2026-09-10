using System;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KairoServer.IntegrationTests
{
    /// <summary>
    /// Fixture for Delivery repository tests. Spins up Postgres and applies the
    /// full migration chain (events, outbox, customers, contracts, deliveries).
    /// </summary>
    public sealed class DeliveriesFixture : IAsyncLifetime
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

        public async Task DisposeAsync()
        {
            await _postgres.DisposeAsync();
        }

        public async Task ResetAsync()
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "TRUNCATE TABLE deliveries, contract_event_types, contracts, customers, events CASCADE;");
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
            })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(migrationsDir, file));
                await conn.ExecuteAsync(sql);
            }
        }
    }

    [CollectionDefinition(nameof(DeliveriesCollection))]
    public sealed class DeliveriesCollection : ICollectionFixture<DeliveriesFixture> { }
}

using System;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NuntivusServer.IntegrationTests
{
    /// <summary>
    /// Fixture for Delivery repository tests. Spins up Postgres and applies the
    /// full migration chain (events, outbox, customers, contracts, deliveries).
    /// </summary>
    public sealed class DeliveriesFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("nuntivus_it")
            .WithUsername("nuntivus")
            .WithPassword("nuntivus")
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
                "TRUNCATE TABLE event_definitions, delivery_attempts, deliveries, contract_event_types, contracts, customers, events CASCADE;");
            await conn.ExecuteAsync("TRUNCATE TABLE managed_secrets CASCADE;");
            await ManagedSecretTestSeed.SeedAsync(conn, "secret.acme.hmac");
        }

        private async Task ApplyMigrationsAsync()
        {
            await MigrationTestHelper.ApplyAllAsync(ConnectionString);
        }
    }

    [CollectionDefinition(nameof(DeliveriesCollection))]
    public sealed class DeliveriesCollection : ICollectionFixture<DeliveriesFixture> { }
}

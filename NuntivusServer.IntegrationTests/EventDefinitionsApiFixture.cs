using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NuntivusServer.IntegrationTests
{
    public sealed class EventDefinitionsApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
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

        public new async Task DisposeAsync()
        {
            await _postgres.DisposeAsync();
            await base.DisposeAsync();
        }

        public async Task ResetAsync()
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync("TRUNCATE TABLE event_schema_versions, event_definitions;");
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
            await MigrationTestHelper.ApplyAllAsync(ConnectionString);
        }
    }

    [CollectionDefinition(nameof(EventDefinitionsApiCollection))]
    public sealed class EventDefinitionsApiCollection : ICollectionFixture<EventDefinitionsApiFixture> { }
}
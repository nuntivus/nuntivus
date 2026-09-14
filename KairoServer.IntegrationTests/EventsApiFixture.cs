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
    public sealed class EventsApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
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
            await conn.ExecuteAsync("TRUNCATE TABLE event_replays, outbox_messages, events CASCADE;");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = ConnectionString
                });
            });
        }

        private async Task ApplyMigrationsAsync()
        {
            var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");

            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            foreach (var file in new[] { "001_create_events.sql", "002_create_outbox_messages.sql", "007_create_event_replays.sql" })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(migrationsDir, file));
                await conn.ExecuteAsync(sql);
            }
        }
    }

    [CollectionDefinition(nameof(EventsApiCollection))]
    public sealed class EventsApiCollection : ICollectionFixture<EventsApiFixture> { }
}
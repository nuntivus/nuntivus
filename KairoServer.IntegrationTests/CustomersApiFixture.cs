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
    /// Shared fixture spinning up a real Postgres container and hosting the API
    /// via WebApplicationFactory. All tests in the collection share the container.
    /// </summary>
    public sealed class CustomersApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
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
            await conn.ExecuteAsync("TRUNCATE TABLE customers;");
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
            var sqlPath = Path.Combine(AppContext.BaseDirectory, "Migrations", "003_create_customers.sql");
            var sql = await File.ReadAllTextAsync(sqlPath);

            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync(sql);
        }
    }

    [CollectionDefinition(nameof(CustomersApiCollection))]
    public sealed class CustomersApiCollection : ICollectionFixture<CustomersApiFixture> { }
}

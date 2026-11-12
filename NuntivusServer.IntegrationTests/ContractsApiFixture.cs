using System.Threading.Tasks;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NuntivusServer.IntegrationTests
{
    /// <summary>
    /// Fixture for Contracts API tests. Spins up Postgres, applies the customers
    /// and contracts migrations, and hosts the API via WebApplicationFactory.
    /// </summary>
    public sealed class ContractsApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public static readonly Guid SourceId = Guid.Parse("b58d1e9e-9035-4e4a-9876-6b661159720e");
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
            // contract_event_types cascades from contracts; contracts cascades from customers.
            await conn.ExecuteAsync("TRUNCATE TABLE customers CASCADE;");
            await conn.ExecuteAsync("TRUNCATE TABLE events CASCADE;");
            await conn.ExecuteAsync("TRUNCATE TABLE delivery_slo_observations;");
            await conn.ExecuteAsync("TRUNCATE TABLE managed_secrets CASCADE;");
            await conn.ExecuteAsync("TRUNCATE TABLE sources CASCADE;");
            await conn.ExecuteAsync("""
                INSERT INTO sources (id, name, type, enabled, configuration, created_at, updated_at)
                VALUES (@Id, 'Integration source', 'HTTP_API', true, '{"authentication":{"type":"none"}}'::jsonb, now(), now())
                """, new { Id = SourceId });
            await conn.ExecuteAsync("TRUNCATE TABLE event_schema_versions, event_definitions;");
            await conn.ExecuteAsync("TRUNCATE TABLE audit_logs;");
            await ManagedSecretTestSeed.SeedAsync(conn,
                "secret.acme.webhook", "secret.acme.rotated", "secret.acme.v1", "secret.acme.v2");
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

            builder.ConfigureServices(services =>
            {
                services.AddTransient<IStartupFilter, TestPrincipalStartupFilter>();
            });
        }

        private async Task ApplyMigrationsAsync()
        {
            await MigrationTestHelper.ApplyAllAsync(ConnectionString);
        }

        private sealed class TestPrincipalStartupFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            {
                return app =>
                {
                    app.Use(async (context, nextMiddleware) =>
                    {
                        if (context.Request.Headers.TryGetValue("X-Test-Auth", out var value) && value == "on")
                        {
                            var operatorId = context.Request.Headers.TryGetValue("X-Operator-Id", out var op)
                                ? op.ToString()
                                : "operator-it";
                            var role = context.Request.Headers.TryGetValue("X-Test-Role", out var requestedRole)
                                ? requestedRole.ToString()
                                : "Operator";

                            var identity = new ClaimsIdentity(
                            [
                                new Claim(ClaimTypes.NameIdentifier, operatorId),
                                new Claim(ClaimTypes.Name, operatorId),
                                new Claim(ClaimTypes.Role, role),
                            ],
                            "TestAuth");
                            context.User = new ClaimsPrincipal(identity);
                        }

                        await nextMiddleware();
                    });

                    next(app);
                };
            }
        }
    }

    [CollectionDefinition(nameof(ContractsApiCollection))]
    public sealed class ContractsApiCollection : ICollectionFixture<ContractsApiFixture> { }
}

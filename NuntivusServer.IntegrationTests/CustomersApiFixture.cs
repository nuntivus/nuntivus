using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
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
    /// Shared fixture spinning up a real Postgres container and hosting the API
    /// via WebApplicationFactory. All tests in the collection share the container.
    /// </summary>
    public sealed class CustomersApiFixture : WebApplicationFactory<NuntivusServer.Api.ApiAssemblyMarker>, IAsyncLifetime
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

        public new async Task DisposeAsync()
        {
            await _postgres.DisposeAsync();
            await base.DisposeAsync();
        }

        public async Task ResetAsync()
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync("TRUNCATE TABLE audit_logs;");
            await conn.ExecuteAsync("TRUNCATE TABLE customers CASCADE;");
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

    [CollectionDefinition(nameof(CustomersApiCollection))]
    public sealed class CustomersApiCollection : ICollectionFixture<CustomersApiFixture> { }
}

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
    public sealed class EventDefinitionsApiFixture : WebApplicationFactory<NuntivusServer.Api.ApiAssemblyMarker>, IAsyncLifetime
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
                            var identity = new ClaimsIdentity(
                            [
                                new Claim(ClaimTypes.NameIdentifier, "operator-it"),
                                new Claim(ClaimTypes.Name, "operator-it"),
                                new Claim(ClaimTypes.Role, "Operator"),
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

    [CollectionDefinition(nameof(EventDefinitionsApiCollection))]
    public sealed class EventDefinitionsApiCollection : ICollectionFixture<EventDefinitionsApiFixture> { }
}
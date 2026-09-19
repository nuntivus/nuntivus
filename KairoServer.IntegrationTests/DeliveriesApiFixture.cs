using System.IO;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KairoServer.IntegrationTests
{
    public sealed class DeliveriesApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
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
            await conn.ExecuteAsync(
                "TRUNCATE TABLE delivery_operation_audits, delivery_attempts, deliveries, contract_event_types, contracts, customers, events CASCADE;");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = ConnectionString,
                });
            });

            builder.ConfigureServices(services =>
            {
                services.AddTransient<IStartupFilter, TestPrincipalStartupFilter>();
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
                "003_create_customers.sql",
                "004_create_contracts.sql",
                "005_create_deliveries.sql",
                "006_create_delivery_attempts.sql",
                "009_create_contract_filters.sql",
                "010_add_contract_payload_transformations.sql",
                "011_add_delivery_rate_limits.sql",
                "012_add_delivery_ordering.sql",
                "013_add_retention_cleanup_indexes.sql",
                "016_add_delivery_contract_snapshots.sql",
                "017_add_contract_snapshot_columns.sql",
                "018_add_delivery_scheduler_indexes.sql",
                "019_add_delivery_operation_audits.sql",
            })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(migrationsDir, file));
                await conn.ExecuteAsync(sql);
            }
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

                            var identity = new ClaimsIdentity(
                            [
                                new Claim(ClaimTypes.NameIdentifier, operatorId),
                                new Claim(ClaimTypes.Name, operatorId),
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

    [CollectionDefinition(nameof(DeliveriesApiCollection))]
    public sealed class DeliveriesApiCollection : ICollectionFixture<DeliveriesApiFixture> { }
}

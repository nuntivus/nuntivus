using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using KairoServer.Application.Caching;
using KairoServer.Application.Matching;
using KairoServer.Application.Messaging;
using KairoServer.Application.Persistence;
using KairoServer.Common.Logging;
using KairoServer.Infastructure.Caching;
using KairoServer.Infastructure.Logging;
using KairoServer.Infastructure.Messaging.Nats;
using KairoServer.Infastructure.Persistence.Caching;
using KairoServer.Infastructure.Persistence.Postgres;
using KairoServer.Worker.Matching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KairoServer.IntegrationTests
{
    /// <summary>
    /// Spins up Postgres + NATS (JetStream) containers, applies migrations, and
    /// exposes helpers to build a Worker-style DI container and to run the
    /// <see cref="ContractMatcherWorker"/> hosted service for end-to-end tests.
    /// </summary>
    public sealed class MatcherFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("kairo_it")
            .WithUsername("kairo")
            .WithPassword("kairo")
            .Build();

        private readonly IContainer _nats = new ContainerBuilder()
            .WithImage("nats:2.10-alpine")
            .WithCommand("-js")
            .WithPortBinding(4222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(4222))
            .Build();

        public string ConnectionString => _postgres.GetConnectionString();

        public string NatsUrl => $"nats://{_nats.Hostname}:{_nats.GetMappedPublicPort(4222)}";

        public async Task InitializeAsync()
        {
            await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync());
            await ApplyMigrationsAsync();
        }

        public async Task DisposeAsync()
        {
            await _postgres.DisposeAsync();
            await _nats.DisposeAsync();
        }

        public async Task ResetAsync()
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            // deliveries cascades from contracts/customers/events; customers cascades cover contracts.
            await conn.ExecuteAsync("TRUNCATE TABLE event_definitions, delivery_attempts, deliveries, outbox_messages, events, contract_event_types, contracts, customers CASCADE;");
        }

        /// <summary>
        /// Builds a ServiceProvider mirroring KairoServer.Worker/Program.cs so
        /// tests can resolve the matcher service or host the worker.
        /// </summary>
        public ServiceProvider BuildServiceProvider(string? matcherDurable = null)
        {
            var services = new ServiceCollection();

            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning).AddConsole());
            services.AddSingleton(typeof(IAppLogger<>), typeof(NullAppLogger<>));

            services.AddSingleton<IDatabaseConnectionFactory>(
                new PostgresDatabaseConnectionFactory(ConnectionString));

            var cacheOption = new CacheOption { Ttl = TimeSpan.FromSeconds(60) };
            services.AddSingleton(cacheOption);
            services.AddMemoryCache();
            services.AddSingleton<ICacheStore, InMemoryCacheStore>();

            services.AddScoped<IUnitOfWork, PostgresUnitOfWork>();

            services.AddScoped<PostgresEventRepository>();
            services.AddScoped<IEventRepository>(sp => new CachedEventRepository(
                sp.GetRequiredService<PostgresEventRepository>(),
                sp.GetRequiredService<ICacheStore>(),
                sp.GetRequiredService<CacheOption>()));

            services.AddScoped<PostgresContractRepository>();
            services.AddScoped<IContractRepository>(sp => new CachedContractRepository(
                sp.GetRequiredService<PostgresContractRepository>(),
                sp.GetRequiredService<ICacheStore>(),
                sp.GetRequiredService<CacheOption>()));

            services.AddScoped<PostgresCustomerRepository>();
            services.AddScoped<ICustomerRepository>(sp => new CachedCustomerRepository(
                sp.GetRequiredService<PostgresCustomerRepository>(),
                sp.GetRequiredService<ICacheStore>(),
                sp.GetRequiredService<CacheOption>()));

            services.AddScoped<IDeliveryRepository, PostgresDeliveryRepository>();
            services.AddScoped<IOutboxRepository, PostgresOutboxRepository>();
            services.AddSingleton<IOutboxReader, PostgresOutboxReader>();
            services.AddSingleton<IContractFilterEvaluator, ContractFilterEvaluator>();
            services.AddScoped<IContractMatcherService, ContractMatcherService>();

            var natsOption = new NatsOption
            {
                Url = NatsUrl,
                Name = "kairo-tests",
                Stream = "KAIRO_EVENTS",
                Subjects = new[] { "event.>", "delivery.>" },
            };
            services.AddSingleton(natsOption);

            var matcherOption = new MatcherOption
            {
                DurableName = matcherDurable ?? "contract-matcher",
                BatchSize = 16,
                FetchTimeout = TimeSpan.FromSeconds(1),
                NakDelay = TimeSpan.FromMilliseconds(200),
            };
            services.AddSingleton(matcherOption);

            services.AddSingleton<INatsConnection>(sp =>
                new NatsConnection(NatsOpts.Default with { Url = natsOption.Url, Name = natsOption.Name }));
            services.AddSingleton<INatsJSContext>(sp =>
                new NatsJSContext(sp.GetRequiredService<INatsConnection>()));

            services.AddSingleton<IEventBusPublisher, NatsEventBusPublisher>();

            return services.BuildServiceProvider();
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
                     })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(migrationsDir, file));
                await conn.ExecuteAsync(sql);
            }
        }

        private sealed class NullAppLogger<T> : IAppLogger<T>
        {
            public void Debug(string messageTemplate, params object?[] args) { }
            public void Information(string messageTemplate, params object?[] args) { }
            public void Warning(string messageTemplate, params object?[] args) { }
            public void Error(string messageTemplate, params object?[] args) { }
            public void Error(Exception exception, string messageTemplate, params object?[] args) { }
            public void Fatal(string messageTemplate, params object?[] args) { }
            public void Fatal(Exception exception, string messageTemplate, params object?[] args) { }
        }
    }

    [CollectionDefinition(nameof(MatcherCollection))]
    public sealed class MatcherCollection : ICollectionFixture<MatcherFixture> { }
}

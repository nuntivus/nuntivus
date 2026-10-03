using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NuntivusServer.Application.Caching;
using NuntivusServer.Application.Matching;
using NuntivusServer.Application.Messaging;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common.Logging;
using NuntivusServer.Domain.Deliveries;
using NuntivusServer.Infastructure.Caching;
using NuntivusServer.Infastructure.Logging;
using NuntivusServer.Infastructure.Messaging.Nats;
using NuntivusServer.Infastructure.Persistence.Caching;
using NuntivusServer.Infastructure.Persistence.Postgres;
using NuntivusServer.Worker.Matching;
using NuntivusServer.Worker.Deliveries;
using NuntivusServer.Worker.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NuntivusServer.IntegrationTests
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
            .WithDatabase("nuntivus_it")
            .WithUsername("nuntivus")
            .WithPassword("nuntivus")
            .Build();

        private readonly IContainer _nats = new ContainerBuilder()
            .WithImage("nats:2.10-alpine")
            .WithCommand("-js")
            .WithPortBinding(4222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(4222))
            .Build();

        public string ConnectionString => _postgres.GetConnectionString();

        public static readonly Guid TestSourceId = Guid.Parse("ebc7ce26-7e11-4125-b5c1-37c853bb5710");

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
            await conn.ExecuteAsync("TRUNCATE TABLE managed_secrets CASCADE;");
            await conn.ExecuteAsync("TRUNCATE TABLE sources CASCADE;");
            await conn.ExecuteAsync("INSERT INTO sources (id, name, type, enabled, configuration, created_at, updated_at) VALUES (@Id, 'Test source', 'HTTP_API', TRUE, '{\"authentication\":{\"type\":\"none\"}}'::jsonb, now(), now())", new { Id = TestSourceId });
            await ManagedSecretTestSeed.SeedAsync(conn, "secret.acme.v1", "secret.acme.v2");
        }

        /// <summary>
        /// Builds a ServiceProvider mirroring NuntivusServer.Worker/Program.cs so
        /// tests can resolve the matcher service or host the worker.
        /// </summary>
        public ServiceProvider BuildServiceProvider(
            string? matcherDurable = null,
            Action<IServiceCollection>? configureServices = null)
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
            services.AddScoped<IAuditLogRepository, PostgresAuditLogRepository>();

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
            services.AddSingleton<IDeliveryFactory, DeliveryFactory>();
            services.AddScoped<IOutboxRepository, PostgresOutboxRepository>();
            services.AddSingleton<IOutboxReader, PostgresOutboxReader>();
            services.AddSingleton<IContractFilterEvaluator, ContractFilterEvaluator>();
            services.AddScoped<IContractMatcherService, ContractMatcherService>();

            var natsOption = new NatsOption
            {
                Url = NatsUrl,
                Name = "nuntivus-tests",
                Stream = "NUNTIVUS_EVENTS",
                Subjects = new[] { "event.>", "delivery.>" },
            };
            services.AddSingleton(natsOption);
            services.AddSingleton<IDurableConsumerNamer, NuntivusDurableConsumerNamer>();

            var matcherOption = new MatcherOption
            {
                DurableName = matcherDurable ?? "contract-matcher",
                BatchSize = 16,
                MaxConcurrentMessages = 4,
                FetchTimeout = TimeSpan.FromSeconds(1),
                NakDelay = TimeSpan.FromMilliseconds(200),
            };
            services.AddSingleton(matcherOption);

            services.AddSingleton(new OutboxOption
            {
                BatchSize = 32,
                PollDelay = TimeSpan.FromMilliseconds(100),
                MaxConcurrentPublishes = 4,
                ClaimTtl = TimeSpan.FromSeconds(30),
            });

            services.AddSingleton(new DeliverySchedulerOption
            {
                BatchSize = 32,
                PollDelay = TimeSpan.FromMilliseconds(100),
                EmptyPollDelay = TimeSpan.FromMilliseconds(150),
            });

            services.AddSingleton<INatsConnection>(sp =>
                new NatsConnection(NatsOpts.Default with { Url = natsOption.Url, Name = natsOption.Name }));
            services.AddSingleton<INatsJSContext>(sp =>
                new NatsJSContext(sp.GetRequiredService<INatsConnection>()));
            services.AddSingleton<IDurableConsumerFactory, NatsDurableConsumerFactory>();

            services.AddSingleton<IEventBusPublisher, NatsEventBusPublisher>();

            configureServices?.Invoke(services);

            return services.BuildServiceProvider();
        }

        public Task StopPostgresAsync() => _postgres.StopAsync();

        public Task StartPostgresAsync() => _postgres.StartAsync();

        public Task StopNatsAsync() => _nats.StopAsync();

        public Task StartNatsAsync() => _nats.StartAsync();

        private async Task ApplyMigrationsAsync()
        {
            await MigrationTestHelper.ApplyAllAsync(ConnectionString);
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

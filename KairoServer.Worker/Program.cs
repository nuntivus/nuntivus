using KairoServer.Application.Caching;
using KairoServer.Application.Matching;
using KairoServer.Application.Messaging;
using KairoServer.Application.Persistence;
using KairoServer.Infastructure.Caching;
using KairoServer.Infastructure.Logging;
using KairoServer.Infastructure.Messaging.Nats;
using KairoServer.Infastructure.Persistence.Caching;
using KairoServer.Infastructure.Persistence.Postgres;
using KairoServer.Worker.Matching;
using KairoServer.Worker.Outbox;
using NATS.Client.Core;
using NATS.Client.JetStream;

var builder = Host.CreateApplicationBuilder(args);

// Logging
builder.AddKairoLogging();

// Persistence
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres configuration.");

builder.Services.AddSingleton<IDatabaseConnectionFactory>(
    _ => new PostgresDatabaseConnectionFactory(postgresConnectionString));

builder.Services.AddSingleton<IOutboxReader, PostgresOutboxReader>();

// Caching (in-memory, 60s TTL by default; configurable via "Cache" section).
var cacheOption = builder.Configuration.GetSection("Cache").Get<CacheOption>() ?? new CacheOption();
builder.Services.AddSingleton(cacheOption);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheStore, InMemoryCacheStore>();

// Matcher pipeline: scoped repos + UoW, wrapped in TTL cache decorators.
builder.Services.AddScoped<IUnitOfWork, PostgresUnitOfWork>();

builder.Services.AddScoped<PostgresEventRepository>();
builder.Services.AddScoped<IEventRepository>(sp => new CachedEventRepository(
    sp.GetRequiredService<PostgresEventRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<PostgresContractRepository>();
builder.Services.AddScoped<IContractRepository>(sp => new CachedContractRepository(
    sp.GetRequiredService<PostgresContractRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<PostgresCustomerRepository>();
builder.Services.AddScoped<ICustomerRepository>(sp => new CachedCustomerRepository(
    sp.GetRequiredService<PostgresCustomerRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<IDeliveryRepository, PostgresDeliveryRepository>();

builder.Services.AddScoped<IContractMatcherService, ContractMatcherService>();

// NATS / JetStream
var natsOption = builder.Configuration.GetSection("Nats").Get<NatsOption>()
    ?? new NatsOption();
builder.Services.AddSingleton(natsOption);

var matcherOption = builder.Configuration.GetSection("Matcher").Get<MatcherOption>()
    ?? new MatcherOption();
builder.Services.AddSingleton(matcherOption);

builder.Services.AddSingleton<INatsConnection>(sp =>
{
    var options = sp.GetRequiredService<NatsOption>();
    var opts = NatsOpts.Default with
    {
        Url = options.Url,
        Name = options.Name ?? "kairo-worker",
        AuthOpts = NatsAuthOpts.Default with
        {
            Token = options.Token,
            Username = options.User,
            Password = options.Password,
            CredsFile = options.CredsFile,
        },
        ConnectTimeout = options.ConnectTimeout,
    };

    return new NatsConnection(opts);
});

builder.Services.AddSingleton<INatsJSContext>(sp =>
    new NatsJSContext(sp.GetRequiredService<INatsConnection>()));

builder.Services.AddSingleton<IEventBusPublisher, NatsEventBusPublisher>();

// Ensure the JetStream stream exists before publishers start.
builder.Services.AddHostedService<NatsJetStreamBootstrapper>();

// Hosted workers
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddHostedService<ContractMatcherWorker>();

var host = builder.Build();
host.Run();

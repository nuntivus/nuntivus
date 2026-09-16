using KairoServer.Application.Caching;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Deliveries.Recovery;
using KairoServer.Application.Matching;
using KairoServer.Application.Messaging;
using KairoServer.Application.Observability;
using KairoServer.Application.Persistence;
using KairoServer.Infastructure.Caching;
using KairoServer.Infastructure.Deliveries;
using KairoServer.Infastructure.Deliveries.Http;
using KairoServer.Infastructure.Logging;
using KairoServer.Infastructure.Messaging.Nats;
using KairoServer.Infastructure.Persistence.Caching;
using KairoServer.Infastructure.Persistence.Postgres;
using KairoServer.Worker.Deliveries;
using KairoServer.Worker.Matching;
using KairoServer.Worker.Outbox;
using NATS.Client.Core;
using NATS.Client.JetStream;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Logging
builder.AddKairoLogging();

var serviceName = "kairo-worker";
var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(KairoTelemetry.ActivitySourceName)
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(KairoTelemetry.MeterName);

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

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
builder.Services.AddScoped<IContractDeliveryRateLimiter, PostgresContractDeliveryRateLimiter>();
builder.Services.AddSingleton<IDeliveryDispatcherMetrics, MeterDeliveryDispatcherMetrics>();

builder.Services.AddSingleton<IContractFilterEvaluator, ContractFilterEvaluator>();
builder.Services.AddScoped<IContractMatcherService, ContractMatcherService>();

// Delivery dispatcher pipeline options.
var deliveryOption = builder.Configuration.GetSection("Delivery").Get<DeliveryOption>()
    ?? new DeliveryOption();
builder.Services.AddSingleton(deliveryOption);

var dispatcherOption = builder.Configuration.GetSection("DeliveryDispatcher").Get<DeliveryDispatcherOption>()
    ?? new DeliveryDispatcherOption();
builder.Services.AddSingleton(dispatcherOption);

var orderingOption = builder.Configuration.GetSection("DeliveryOrdering").Get<DeliveryOrderingOption>()
    ?? new DeliveryOrderingOption();
builder.Services.AddSingleton(orderingOption);

var destinationCredentialOption = builder.Configuration.GetSection("DestinationCredentials").Get<DestinationCredentialOption>()
    ?? new DestinationCredentialOption();
builder.Services.AddSingleton(destinationCredentialOption);

builder.Services.AddScoped<IDeliveryDispatcherService, DeliveryDispatcherService>();
builder.Services.AddScoped<IDeliveryRecoveryService, DeliveryRecoveryService>();

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

// Delivery execution boundary (HTTP transport for now).
builder.Services.AddDeliveryExecution();

// Ensure the JetStream stream exists before publishers start.
builder.Services.AddHostedService<NatsJetStreamBootstrapper>();

// Hosted workers
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddHostedService<ContractMatcherWorker>();
builder.Services.AddHostedService<DeliveryDispatcherWorker>();

var host = builder.Build();
host.Run();

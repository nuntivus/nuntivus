using KairoServer.Application.Caching;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Deliveries.Recovery;
using KairoServer.Application.Matching;
using KairoServer.Application.Messaging;
using KairoServer.Application.Observability;
using KairoServer.Application.Persistence;
using KairoServer.Application.Retention;
using KairoServer.Application.Security;
using KairoServer.Domain.Deliveries;
using KairoServer.Infastructure.Caching;
using KairoServer.Infastructure.Configuration;
using KairoServer.Infastructure.Deliveries;
using KairoServer.Infastructure.Deliveries.Http;
using KairoServer.Infastructure.Logging;
using KairoServer.Infastructure.Messaging.Nats;
using KairoServer.Infastructure.Persistence.Caching;
using KairoServer.Infastructure.Persistence.Postgres;
using KairoServer.Worker.Configuration;
using KairoServer.Worker.Deliveries;
using KairoServer.Worker.Matching;
using KairoServer.Worker.Outbox;
using KairoServer.Worker.Retention;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddKairoEnvironmentVariableMappings();

builder.Services.AddSingleton<IDurableConsumerNamer, KairoDurableConsumerNamer>();

builder.Services
    .AddOptions<HostOptions>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.HostOptions))
    .PostConfigure(options =>
    {
        if (options.ShutdownTimeout <= TimeSpan.Zero)
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        }
    })
    .Validate(options => options.ShutdownTimeout > TimeSpan.Zero, "HostOptions:ShutdownTimeout must be greater than zero.")
    .ValidateOnStart();

builder.Services
    .AddOptions<DatabaseOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.Database))
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.PostgresConnectionString))
        {
            options.PostgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
        }
    })
    .Validate(KairoConfigurationValidation.HasPostgresConnection, "Configure Database:PostgresConnectionString (or ConnectionStrings:Postgres).")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DatabaseOption>>().Value);

builder.Services
    .AddOptions<CacheOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.Cache))
    .Validate(KairoConfigurationValidation.IsCacheTtlValid, "Cache:Ttl must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<CacheOption>>().Value);

builder.Services
    .AddOptions<DeliveryOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.Delivery))
    .Validate(WorkerConfigurationValidation.IsDeliveryConsumerValid, "Delivery configuration is invalid.")
    .Validate<NatsOption, IDurableConsumerNamer>(
        WorkerConfigurationValidation.IsDeliveryConsumerTopologyValid,
        "Delivery consumer configuration must map to Nats:Subjects and a valid durable name.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DeliveryOption>>().Value);

builder.Services
    .AddOptions<DeliveryDispatcherOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.DeliveryDispatcher))
    .Validate(KairoConfigurationValidation.IsRetryPolicyValid, "DeliveryDispatcher retry configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DeliveryDispatcherOption>>().Value);

builder.Services
    .AddOptions<DeliveryOrderingOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.DeliveryOrdering))
    .Validate(KairoConfigurationValidation.IsOrderingValid, "DeliveryOrdering configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DeliveryOrderingOption>>().Value);

builder.Services
    .AddOptions<DestinationCredentialOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.DestinationCredentials))
    .Validate(KairoConfigurationValidation.IsDestinationCredentialKeyValid, "DestinationCredentials:EncryptionKey must be base64 and decode to 32 bytes.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DestinationCredentialOption>>().Value);

builder.Services
    .AddOptions<HttpDestinationSecurityOption>()
    .Configure(options =>
    {
        var endpointSection = builder.Configuration.GetSection(KairoConfigurationSections.EndpointSecurity);
        if (endpointSection.Exists())
        {
            endpointSection.Bind(options);
        }
        else
        {
            builder.Configuration.GetSection(KairoConfigurationSections.LegacyDestinationSecurity).Bind(options);
        }

        KairoConfigurationValidation.ApplySafeEndpointDefaults(options, builder.Environment.IsProduction());
    })
    .Validate(KairoConfigurationValidation.IsEndpointSecurityValid, "EndpointSecurity:MaxResponseBodyBytes must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<HttpDestinationSecurityOption>>().Value);

builder.Services
    .AddOptions<RetentionCleanupOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.RetentionCleanup))
    .Validate(KairoConfigurationValidation.IsRetentionValid, "RetentionCleanup configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<RetentionCleanupOption>>().Value);

builder.Services
    .AddOptions<NatsOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.Nats))
    .Validate(KairoConfigurationValidation.IsNatsSettingsValid, "Nats configuration is invalid. Configure URL/stream/timeouts and exactly one auth mode.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<NatsOption>>().Value);

builder.Services
    .AddOptions<MatcherOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.Matcher))
    .Validate(WorkerConfigurationValidation.IsMatcherConsumerValid, "Matcher configuration is invalid.")
    .Validate<NatsOption, IDurableConsumerNamer>(
        WorkerConfigurationValidation.IsMatcherConsumerTopologyValid,
        "Matcher consumer configuration must map to Nats:Subjects and a valid durable name.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MatcherOption>>().Value);

builder.Services
    .AddOptions<OutboxOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.Outbox))
    .Validate(WorkerConfigurationValidation.IsOutboxValid, "Outbox configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<OutboxOption>>().Value);

builder.Services
    .AddOptions<DeliveryRecoveryOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.DeliveryRecovery))
    .Validate(WorkerConfigurationValidation.IsDeliveryRecoveryValid, "DeliveryRecovery configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DeliveryRecoveryOption>>().Value);

builder.Services
    .AddOptions<WorkerRuntimeOption>()
    .Bind(builder.Configuration.GetSection("Worker"))
    .Validate(WorkerConfigurationValidation.IsRuntimeValid, "Worker configuration is invalid. Enable at least one hosted worker.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<WorkerRuntimeOption>>().Value);

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
builder.Services.AddSingleton<IDatabaseConnectionFactory>(
    sp => new PostgresDatabaseConnectionFactory(
        sp.GetRequiredService<DatabaseOption>().PostgresConnectionString!));

builder.Services.AddSingleton<IOutboxReader, PostgresOutboxReader>();
builder.Services.AddSingleton<IRetentionCleanupRepository, PostgresRetentionCleanupRepository>();

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
builder.Services.AddSingleton<IDeliveryFactory, DeliveryFactory>();
builder.Services.AddScoped<IContractDeliveryRateLimiter, PostgresContractDeliveryRateLimiter>();
builder.Services.AddSingleton<IDeliveryDispatcherMetrics, MeterDeliveryDispatcherMetrics>();

builder.Services.AddSingleton<IContractFilterEvaluator, ContractFilterEvaluator>();
builder.Services.AddScoped<IContractMatcherService, ContractMatcherService>();

builder.Services.AddSingleton<IEndpointSecurityPolicy, EndpointSecurityPolicy>();

builder.Services.AddScoped<IDeliveryDispatcherService, DeliveryDispatcherService>();
builder.Services.AddScoped<IDeliveryRecoveryService, DeliveryRecoveryService>();
builder.Services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();

// NATS / JetStream
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

builder.Services.AddSingleton<IDurableConsumerFactory, NatsDurableConsumerFactory>();

builder.Services.AddSingleton<IEventBusPublisher, NatsEventBusPublisher>();

// Delivery execution boundary (HTTP transport for now).
builder.Services.AddDeliveryExecution();

// Ensure the JetStream stream exists before publishers start.
builder.Services.AddHostedService<NatsJetStreamBootstrapper>();

// Hosted workers
var workerRuntime = builder.Configuration.GetSection("Worker").Get<WorkerRuntimeOption>() ?? new WorkerRuntimeOption();
if (workerRuntime.EnableOutboxPublisher)
{
    builder.Services.AddHostedService<OutboxPublisherWorker>();
}

if (workerRuntime.EnableContractMatcher)
{
    builder.Services.AddHostedService<ContractMatcherWorker>();
}

if (workerRuntime.EnableDeliveryDispatcher)
{
    builder.Services.AddHostedService<DeliveryDispatcherWorker>();
}

if (workerRuntime.EnableDeliveryRecovery)
{
    builder.Services.AddHostedService<DeliveryRecoveryWorker>();
}

if (workerRuntime.EnableRetentionCleanup)
{
    builder.Services.AddHostedService<RetentionCleanupWorker>();
}

var host = builder.Build();
host.Run();

using NuntivusServer.Application.Caching;
using NuntivusServer.Application.Auditing;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Deliveries.Recovery;
using NuntivusServer.Application.Matching;
using NuntivusServer.Application.Messaging;
using NuntivusServer.Application.Observability;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Retention;
using NuntivusServer.Application.Security;
using NuntivusServer.Domain.Deliveries;
using NuntivusServer.Infastructure.Caching;
using NuntivusServer.Infastructure.Configuration;
using NuntivusServer.Infastructure.Deliveries;
using NuntivusServer.Infastructure.Deliveries.Http;
using NuntivusServer.Infastructure.Health;
using NuntivusServer.Infastructure.Logging;
using NuntivusServer.Infastructure.Messaging.Nats;
using NuntivusServer.Infastructure.Persistence.Caching;
using NuntivusServer.Infastructure.Persistence.Postgres;
using NuntivusServer.Infastructure.Persistence.Postgres.Migrations;
using NuntivusServer.Worker.Configuration;
using NuntivusServer.Worker.Deliveries;
using NuntivusServer.Worker.Matching;
using NuntivusServer.Worker.Outbox;
using NuntivusServer.Worker.Retention;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["Health:Url"] ?? "http://0.0.0.0:8081");

builder.Configuration.AddNuntivusEnvironmentVariableMappings();

builder.Services.AddSingleton<IDurableConsumerNamer, NuntivusDurableConsumerNamer>();

builder.Services
    .AddOptions<HostOptions>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.HostOptions))
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
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.Database))
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.PostgresConnectionString))
        {
            options.PostgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
        }
    })
    .Validate(NuntivusConfigurationValidation.HasPostgresConnection, "Configure Database:PostgresConnectionString (or ConnectionStrings:Postgres).")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DatabaseOption>>().Value);

builder.Services
    .AddOptions<DatabaseMigrationsOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.DatabaseMigrations))
    .PostConfigure(options =>
    {
        if (options.Mode == DatabaseMigrationMode.Disabled && builder.Environment.IsProduction())
        {
            options.Mode = DatabaseMigrationMode.Validate;
        }
    })
    .Validate(options => !string.IsNullOrWhiteSpace(options.MigrationsPath), "DatabaseMigrations:MigrationsPath must not be empty.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DatabaseMigrationsOption>>().Value);

builder.Services
    .AddOptions<CacheOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.Cache))
    .Validate(NuntivusConfigurationValidation.IsCacheTtlValid, "Cache:Ttl must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<CacheOption>>().Value);

builder.Services
    .AddOptions<DeliveryOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.Delivery))
    .Validate(WorkerConfigurationValidation.IsDeliveryConsumerValid, "Delivery configuration is invalid.")
    .Validate<NatsOption, IDurableConsumerNamer>(
        WorkerConfigurationValidation.IsDeliveryConsumerTopologyValid,
        "Delivery consumer configuration must map to Nats:Subjects and a valid durable name.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DeliveryOption>>().Value);

builder.Services
    .AddOptions<DeliveryDispatcherOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.DeliveryDispatcher))
    .Validate(NuntivusConfigurationValidation.IsRetryPolicyValid, "DeliveryDispatcher retry configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DeliveryDispatcherOption>>().Value);

builder.Services
    .AddOptions<DeliverySchedulerOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.DeliveryScheduler))
    .Validate(WorkerConfigurationValidation.IsDeliverySchedulerValid, "DeliveryScheduler configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DeliverySchedulerOption>>().Value);

builder.Services
    .AddOptions<DeliveryOrderingOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.DeliveryOrdering))
    .Validate(NuntivusConfigurationValidation.IsOrderingValid, "DeliveryOrdering configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DeliveryOrderingOption>>().Value);

builder.Services
    .AddOptions<DestinationCredentialOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.DestinationCredentials))
    .Validate(NuntivusConfigurationValidation.IsDestinationCredentialKeyValid, "DestinationCredentials:EncryptionKey must be base64 and decode to 32 bytes.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DestinationCredentialOption>>().Value);

builder.Services
    .AddOptions<HttpDestinationSecurityOption>()
    .Configure(options =>
    {
        var endpointSection = builder.Configuration.GetSection(NuntivusConfigurationSections.EndpointSecurity);
        if (endpointSection.Exists())
        {
            endpointSection.Bind(options);
        }
        else
        {
            builder.Configuration.GetSection(NuntivusConfigurationSections.LegacyDestinationSecurity).Bind(options);
        }

        NuntivusConfigurationValidation.ApplySafeEndpointDefaults(options, builder.Environment.IsProduction());
    })
    .Validate(NuntivusConfigurationValidation.IsEndpointSecurityValid, "EndpointSecurity:MaxResponseBodyBytes, MaxHeaders, and MaxConfigurationBytes must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<HttpDestinationSecurityOption>>().Value);

builder.Services
    .AddOptions<RetentionCleanupOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.RetentionCleanup))
    .Validate(NuntivusConfigurationValidation.IsRetentionValid, "RetentionCleanup configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<RetentionCleanupOption>>().Value);

builder.Services
    .AddOptions<NatsOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.Nats))
    .Validate(NuntivusConfigurationValidation.IsNatsSettingsValid, "Nats configuration is invalid. Configure URL/stream/timeouts and exactly one auth mode.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<NatsOption>>().Value);

builder.Services
    .AddOptions<MatcherOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.Matcher))
    .Validate(WorkerConfigurationValidation.IsMatcherConsumerValid, "Matcher configuration is invalid.")
    .Validate<NatsOption, IDurableConsumerNamer>(
        WorkerConfigurationValidation.IsMatcherConsumerTopologyValid,
        "Matcher consumer configuration must map to Nats:Subjects and a valid durable name.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MatcherOption>>().Value);

builder.Services
    .AddOptions<OutboxOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.Outbox))
    .Validate(WorkerConfigurationValidation.IsOutboxValid, "Outbox configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<OutboxOption>>().Value);

builder.Services
    .AddOptions<DeliveryRecoveryOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.DeliveryRecovery))
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
builder.AddNuntivusLogging();

var serviceName = "nuntivus-worker";
var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(NuntivusTelemetry.ActivitySourceName)
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(NuntivusTelemetry.MeterName);

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

// Persistence
builder.Services.AddSingleton<IDatabaseConnectionFactory>(
    sp => new PostgresDatabaseConnectionFactory(
        sp.GetRequiredService<DatabaseOption>().PostgresConnectionString!));
builder.Services.AddSingleton<IPostgresSchemaMigrationRunner, PostgresSchemaMigrationRunner>();

builder.Services.AddSingleton<IOutboxReader, PostgresOutboxReader>();
builder.Services.AddScoped<IOutboxRepository, PostgresOutboxRepository>();
builder.Services.AddSingleton<IRetentionCleanupRepository, PostgresRetentionCleanupRepository>();
builder.Services.AddScoped<IAuditLogRepository, PostgresAuditLogRepository>();
builder.Services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();

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
builder.Services.AddScoped<IDeliveryOrderingCoordinator, PostgresDeliveryOrderingCoordinator>();
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
        Name = options.Name ?? "nuntivus-worker",
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

builder.Services.AddSingleton(new RequiredTablesReadinessOption
{
    Schema = "public",
    Tables = RequiredTablesReadinessDefaults.CoreTables,
});

builder.Services.AddHealthChecks()
    .AddCheck<PostgresConnectionHealthCheck>("postgres", tags: new[] { "ready" })
    .AddCheck<PostgresRequiredTablesHealthCheck>("migration_tables", tags: new[] { "ready" })
    .AddCheck<NatsConnectionHealthCheck>("nats", tags: new[] { "ready" })
    .AddCheck<JetStreamHealthCheck>("jetstream", tags: new[] { "ready" })
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" });

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

if (workerRuntime.EnableDeliveryScheduler)
{
    builder.Services.AddHostedService<DeliverySchedulerWorker>();
}

if (workerRuntime.EnableDeliveryRecovery)
{
    builder.Services.AddHostedService<DeliveryRecoveryWorker>();
}

if (workerRuntime.EnableRetentionCleanup)
{
    builder.Services.AddHostedService<RetentionCleanupWorker>();
}

var app = builder.Build();

await app.Services.EnsureDatabaseMigrationsAsync();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();

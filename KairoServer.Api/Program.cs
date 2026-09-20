using KairoServer.Application.Caching;
using KairoServer.Application.Contracts;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Customers;
using KairoServer.Application.Deliveries.Queries;
using KairoServer.Application.Deliveries.Recovery;
using KairoServer.Application.Events;
using KairoServer.Application.Events.Queries;
using KairoServer.Application.Persistence;
using KairoServer.Application.Security;
using KairoServer.Api.Common;
using KairoServer.Api.Controllers.Events;
using KairoServer.Infastructure.Configuration;
using KairoServer.Infastructure.Caching;
using KairoServer.Infastructure.Deliveries.Http;
using KairoServer.Infastructure.Health;
using KairoServer.Infastructure.Logging;
using KairoServer.Infastructure.Persistence.Caching;
using KairoServer.Infastructure.Persistence.Postgres;
using KairoServer.Infastructure.Persistence.Postgres.Migrations;
using KairoServer.Domain.Deliveries;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddKairoEnvironmentVariableMappings();

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
    .AddOptions<DatabaseMigrationsOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.DatabaseMigrations))
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
    .AddOptions<ApiSecurityOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.ApiSecurity))
    .Validate(options => options.MaxRequestBodyBytes > 0, "ApiSecurity:MaxRequestBodyBytes must be greater than zero.")
    .Validate(options => options.MaxEventPayloadBytes > 0, "ApiSecurity:MaxEventPayloadBytes must be greater than zero.")
    .Validate(options => options.MaxEventMetadataBytes > 0, "ApiSecurity:MaxEventMetadataBytes must be greater than zero.")
    .Validate(options => options.MaxEventPayloadBytes <= options.MaxRequestBodyBytes, "ApiSecurity:MaxEventPayloadBytes cannot exceed MaxRequestBodyBytes.")
    .Validate(options => options.MaxEventMetadataBytes <= options.MaxRequestBodyBytes, "ApiSecurity:MaxEventMetadataBytes cannot exceed MaxRequestBodyBytes.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ApiSecurityOption>>().Value);

builder.Services
    .AddOptions<CacheOption>()
    .Bind(builder.Configuration.GetSection(KairoConfigurationSections.Cache))
    .Validate(KairoConfigurationValidation.IsCacheTtlValid, "Cache:Ttl must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<CacheOption>>().Value);

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
    .Validate(KairoConfigurationValidation.IsEndpointSecurityValid, "EndpointSecurity:MaxResponseBodyBytes, MaxHeaders, and MaxConfigurationBytes must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<HttpDestinationSecurityOption>>().Value);

builder.Services.AddSingleton<IConfigureOptions<KestrelServerOptions>>(sp =>
    new ConfigureNamedOptions<KestrelServerOptions>(Options.DefaultName, options =>
    {
        var apiSecurity = sp.GetRequiredService<ApiSecurityOption>();
        options.Limits.MaxRequestBodySize = apiSecurity.MaxRequestBodyBytes;
    }));

builder.AddKairoLogging();

builder.Services.AddControllers();

builder.Services.AddSingleton(new RequiredTablesReadinessOption
{
    Schema = "public",
    Tables = RequiredTablesReadinessDefaults.CoreTables,
});

builder.Services.AddHealthChecks()
    .AddCheck<PostgresConnectionHealthCheck>("postgres", tags: new[] { "ready" })
    .AddCheck<PostgresRequiredTablesHealthCheck>("migration_tables", tags: new[] { "ready" })
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IDatabaseConnectionFactory>(
    sp => new PostgresDatabaseConnectionFactory(
        sp.GetRequiredService<DatabaseOption>().PostgresConnectionString!));
builder.Services.AddSingleton<IPostgresSchemaMigrationRunner, PostgresSchemaMigrationRunner>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheStore, InMemoryCacheStore>();

builder.Services.AddScoped<IUnitOfWork, PostgresUnitOfWork>();
builder.Services.AddScoped<IOutboxRepository, PostgresOutboxRepository>();
builder.Services.AddScoped<IAuditLogRepository, PostgresAuditLogRepository>();

builder.Services.AddScoped<PostgresEventRepository>();
builder.Services.AddScoped<IEventRepository>(sp => new CachedEventRepository(
    sp.GetRequiredService<PostgresEventRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<PostgresCustomerRepository>();
builder.Services.AddScoped<ICustomerRepository>(sp => new CachedCustomerRepository(
    sp.GetRequiredService<PostgresCustomerRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<PostgresContractRepository>();
builder.Services.AddScoped<IContractRepository>(sp => new CachedContractRepository(
    sp.GetRequiredService<PostgresContractRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<IDeliveryRepository, PostgresDeliveryRepository>();
builder.Services.AddSingleton<IDeliveryFactory, DeliveryFactory>();

builder.Services.AddScoped<PostgresEventDefinitionRepository>();
builder.Services.AddScoped<IEventDefinitionRepository>(sp => new CachedEventDefinitionRepository(
    sp.GetRequiredService<PostgresEventDefinitionRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IEventQueryService, EventQueryService>();
builder.Services.AddScoped<IEventDefinitionService, EventDefinitionService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IDeliveryQueryService, DeliveryQueryService>();
builder.Services.AddScoped<IDeliveryRecoveryService, DeliveryRecoveryService>();
builder.Services.AddSingleton<IEventPublishRequestValidator, EventPublishRequestValidator>();

builder.Services.AddSingleton<IEndpointSecurityPolicy, EndpointSecurityPolicy>();

builder.Services.AddSingleton<IDestinationCredentialProtector, AesGcmDestinationCredentialProtector>();

builder.Services.AddSingleton<IDestinationConfigHandler, HttpDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigHandler, KafkaDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigHandler, NatsDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigHandler, RabbitMqDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigHandler, SqsDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigDispatcher, DestinationConfigDispatcher>();

var app = builder.Build();

var migrateOnly = args.Any(arg => string.Equals(arg, "--migrate", StringComparison.OrdinalIgnoreCase));
if (migrateOnly)
{
    await app.Services.EnsureDatabaseMigrationsAsync(DatabaseMigrationMode.Apply);
    return;
}

await app.Services.EnsureDatabaseMigrationsAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program { }

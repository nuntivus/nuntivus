using KairoServer.Application.Caching;
using KairoServer.Application.Contracts;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Customers;
using KairoServer.Application.Deliveries.Recovery;
using KairoServer.Application.Events;
using KairoServer.Application.Persistence;
using KairoServer.Application.Security;
using KairoServer.Api.Controllers.Events;
using KairoServer.Infastructure.Caching;
using KairoServer.Infastructure.Deliveries.Http;
using KairoServer.Infastructure.Logging;
using KairoServer.Infastructure.Persistence.Caching;
using KairoServer.Infastructure.Persistence.Postgres;
using KairoServer.Api.Common;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var shutdownTimeout = builder.Configuration.GetValue<TimeSpan?>("HostOptions:ShutdownTimeout")
    ?? TimeSpan.FromSeconds(30);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = shutdownTimeout;
});

builder.AddKairoLogging();

var apiSecurityOption =
    builder.Configuration.GetSection("ApiSecurity").Get<ApiSecurityOption>()
    ?? new ApiSecurityOption();
builder.Services.AddSingleton(apiSecurityOption);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = apiSecurityOption.MaxRequestBodyBytes;
});

builder.Services.AddControllers();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: new[] { "ready" })
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres configuration.");

builder.Services.AddSingleton<IDatabaseConnectionFactory>(
    _ => new PostgresDatabaseConnectionFactory(postgresConnectionString));

// Caching (in-memory, 60s TTL; configurable via "Cache" section).
var cacheOption = builder.Configuration.GetSection("Cache").Get<CacheOption>() ?? new CacheOption();
builder.Services.AddSingleton(cacheOption);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheStore, InMemoryCacheStore>();

builder.Services.AddScoped<IUnitOfWork, PostgresUnitOfWork>();
builder.Services.AddScoped<IOutboxRepository, PostgresOutboxRepository>();

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

builder.Services.AddScoped<PostgresEventDefinitionRepository>();
builder.Services.AddScoped<IEventDefinitionRepository>(sp => new CachedEventDefinitionRepository(
    sp.GetRequiredService<PostgresEventDefinitionRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IEventDefinitionService, EventDefinitionService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IDeliveryRecoveryService, DeliveryRecoveryService>();
builder.Services.AddSingleton<IEventPublishRequestValidator, EventPublishRequestValidator>();

var destinationCredentialOption =
    builder.Configuration.GetSection("DestinationCredentials").Get<DestinationCredentialOption>()
    ?? new DestinationCredentialOption();
builder.Services.AddSingleton(destinationCredentialOption);

var destinationSecurityOption =
    builder.Configuration.GetSection("EndpointSecurity").Get<HttpDestinationSecurityOption>()
    ?? builder.Configuration.GetSection("DestinationSecurity").Get<HttpDestinationSecurityOption>()
    ?? new HttpDestinationSecurityOption();
builder.Services.AddSingleton(destinationSecurityOption);
builder.Services.AddSingleton<IEndpointSecurityPolicy, EndpointSecurityPolicy>();

builder.Services.AddSingleton<IDestinationCredentialProtector, AesGcmDestinationCredentialProtector>();

builder.Services.AddSingleton<IDestinationConfigHandler, HttpDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigDispatcher, DestinationConfigDispatcher>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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

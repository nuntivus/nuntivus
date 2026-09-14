using KairoServer.Application.Caching;
using KairoServer.Application.Contracts;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Customers;
using KairoServer.Application.Deliveries.Recovery;
using KairoServer.Application.Events;
using KairoServer.Application.Persistence;
using KairoServer.Infastructure.Caching;
using KairoServer.Infastructure.Deliveries.Http;
using KairoServer.Infastructure.Logging;
using KairoServer.Infastructure.Persistence.Caching;
using KairoServer.Infastructure.Persistence.Postgres;

var builder = WebApplication.CreateBuilder(args);

builder.AddKairoLogging();

builder.Services.AddControllers();

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

var destinationCredentialOption =
    builder.Configuration.GetSection("DestinationCredentials").Get<DestinationCredentialOption>()
    ?? new DestinationCredentialOption();
builder.Services.AddSingleton(destinationCredentialOption);
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

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program { }

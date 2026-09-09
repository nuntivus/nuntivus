using KairoServer.Application.Customers;
using KairoServer.Application.Events;
using KairoServer.Application.Persistence;
using KairoServer.Infastructure.Logging;
using KairoServer.Infastructure.Persistence.Postgres;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.AddKairoLogging();

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Persistence
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres configuration.");

builder.Services.AddSingleton<IDatabaseConnectionFactory>(
    _ => new PostgresDatabaseConnectionFactory(postgresConnectionString));

builder.Services.AddScoped<IUnitOfWork, PostgresUnitOfWork>();
builder.Services.AddScoped<IEventRepository, PostgresEventRepository>();
builder.Services.AddScoped<IOutboxRepository, PostgresOutboxRepository>();
builder.Services.AddScoped<ICustomerRepository, PostgresCustomerRepository>();

// Application handlers
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();

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

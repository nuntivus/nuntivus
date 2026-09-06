using KairoServer.Application.Messaging;
using KairoServer.Application.Persistence;
using KairoServer.Infastructure.Logging;
using KairoServer.Infastructure.Messaging.Nats;
using KairoServer.Infastructure.Persistence.Postgres;
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

// NATS / JetStream
var natsOption = builder.Configuration.GetSection("Nats").Get<NatsOption>()
    ?? new NatsOption();
builder.Services.AddSingleton(natsOption);

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

var host = builder.Build();
host.Run();

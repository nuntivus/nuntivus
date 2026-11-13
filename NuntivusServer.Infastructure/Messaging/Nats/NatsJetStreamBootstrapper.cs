using NuntivusServer.Common.Logging;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NuntivusServer.Infastructure.Messaging.Nats
{
    /// <summary>
    /// Ensures the configured JetStream stream exists (and matches the desired
    /// configuration) before any publisher tries to use it. Runs once at startup
    /// and is idempotent: creates the stream if missing, updates it otherwise.
    /// </summary>
    public sealed class NatsJetStreamBootstrapper : IHostedService
    {
        private readonly INatsConnection _connection;
        private readonly INatsJSContext _jetStream;
        private readonly NatsOption _options;
        private readonly IAppLogger<NatsJetStreamBootstrapper> _logger;

        public NatsJetStreamBootstrapper(
            INatsConnection connection,
            INatsJSContext jetStream,
            NatsOption options,
            IAppLogger<NatsJetStreamBootstrapper> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _jetStream = jetStream ?? throw new ArgumentNullException(nameof(jetStream));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Information(
                "Connecting to NATS at {Url} to bootstrap JetStream stream {Stream}...",
                _options.Url,
                _options.Stream);

            // Force the (otherwise lazy) NATS connection to open before issuing
            // any JetStream API request, so a broken connection surfaces here
            // instead of silently timing out later on PublishAsync.
            await _connection.ConnectAsync();

            _logger.Information(
                "NATS connection established (state {State}, server {Server}).",
                _connection.ConnectionState,
                _connection.ServerInfo?.Name);

            var config = new StreamConfig(
                name: _options.Stream,
                subjects: _options.Subjects.Distinct(StringComparer.Ordinal).ToArray())
            {
                Retention = StreamConfigRetention.Limits,
                Storage = StreamConfigStorage.File,
                Discard = StreamConfigDiscard.Old,
                NumReplicas = 1,
                DuplicateWindow = _options.DuplicateWindow,
            };

            try
            {
                await _jetStream.GetStreamAsync(_options.Stream, cancellationToken: cancellationToken);

                _logger.Information(
                    "JetStream stream {Stream} already exists. Ensuring configuration is up to date.",
                    _options.Stream);

                await _jetStream.UpdateStreamAsync(config, cancellationToken);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                _logger.Information(
                    "JetStream stream {Stream} not found. Creating with subjects {Subjects}.",
                    _options.Stream,
                    _options.Subjects);

                await _jetStream.CreateStreamAsync(config, cancellationToken);
            }

            _logger.Information("JetStream stream {Stream} is ready.", _options.Stream);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

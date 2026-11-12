using System.Text.Json;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;
using NuntivusServer.Application.Deliveries.Execution;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class NatsSourceConnectionTester : ISourceConnectionTester
    {
        private readonly ISecretResolver _secrets;

        public NatsSourceConnectionTester(ISecretResolver secrets) => _secrets = secrets;

        public SourceType Type => SourceType.NATS;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken)
            => ConnectionProbe.RunAsync(async token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                var config = document.RootElement;
                var secret = await ConnectionProbeCredentials.SourceSecretAsync(config, _secrets, token);
                if (secret.IsFailure) return Result<string>.Fail(secret.Errors);
                var options = NatsOpts.Default with
                {
                    Url = config.GetProperty("url").GetString()!,
                    AuthOpts = NatsAuthOpts.Default with { Token = secret.Value }
                };
                await using var connection = new NatsConnection(options);
                var context = new NatsJSContext(connection);
                await context.GetStreamAsync(config.GetProperty("stream").GetString()!, cancellationToken: token);
                return Result.Success("NATS authenticated and configured stream information is accessible. No consumer created or messages fetched/acknowledged; subject delivery and consume permission are not verified.");
            }, cancellationToken);
    }
}

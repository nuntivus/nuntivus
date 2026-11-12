using System.Text.Json;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;
using NuntivusServer.Application.Deliveries.Execution;
using RabbitMQ.Client;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class RabbitMqSourceConnectionTester : ISourceConnectionTester
    {
        private readonly ISecretResolver _secrets;

        public RabbitMqSourceConnectionTester(ISecretResolver secrets) => _secrets = secrets;

        public SourceType Type => SourceType.RABBITMQ;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken)
            => ConnectionProbe.RunAsync(async token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                var config = document.RootElement;
                var secret = await ConnectionProbeCredentials.SourceSecretAsync(config, _secrets, token);
                if (secret.IsFailure) return Result<string>.Fail(secret.Errors);
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(config.GetProperty("uri").GetString()!),
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                    ContinuationTimeout = TimeSpan.FromSeconds(5),
                    AutomaticRecoveryEnabled = false
                };
                if (config.TryGetProperty("username", out var username)) factory.UserName = username.GetString();
                if (secret.Value is not null) factory.Password = secret.Value;
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var connection = factory.CreateConnection();
                    using var channel = connection.CreateModel();
                    channel.QueueDeclarePassive(config.GetProperty("queue").GetString()!);
                    return Result.Success("RabbitMQ authenticated and configured queue exists. No messages fetched or acknowledged; consume permission is not verified.");
                }, token);
            }, cancellationToken);
    }
}

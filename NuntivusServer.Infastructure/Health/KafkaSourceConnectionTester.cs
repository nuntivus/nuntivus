using System.Text.Json;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;
using NuntivusServer.Application.Deliveries.Execution;
using Confluent.Kafka;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class KafkaSourceConnectionTester : ISourceConnectionTester
    {
        private readonly ISecretResolver _secrets;

        public KafkaSourceConnectionTester(ISecretResolver secrets) => _secrets = secrets;

        public SourceType Type => SourceType.KAFKA;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken)
            => ConnectionProbe.RunAsync(async token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                var config = document.RootElement;
                var secret = await ConnectionProbeCredentials.SourceSecretAsync(config, _secrets, token);
                if (secret.IsFailure) return Result<string>.Fail(secret.Errors);
                var settings = new AdminClientConfig
                {
                    BootstrapServers = config.GetProperty("bootstrapServers").GetString(),
                    SecurityProtocol = secret.Value is null ? SecurityProtocol.Ssl : SecurityProtocol.SaslSsl,
                    SocketTimeoutMs = 5000,
                    AllowAutoCreateTopics = false
                };
                if (secret.Value is not null)
                {
                    settings.SaslMechanism = SaslMechanism.Plain;
                    settings.SaslUsername = config.GetProperty("username").GetString();
                    settings.SaslPassword = secret.Value;
                }
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var client = new AdminClientBuilder(settings).Build();
                    var topic = config.GetProperty("topic").GetString()!;
                    var metadata = client.GetMetadata(TimeSpan.FromSeconds(5));
                    var match = metadata.Topics.SingleOrDefault(value => value.Topic == topic);
                    return match is null || match.Error.IsError || match.Partitions.Any(partition => partition.Error.IsError)
                        ? Result.Failure<string>(NuntivusServer.Common.Error.Validation("Configured Kafka topic is missing or its metadata is not accessible."))
                        : Result.Success("Kafka authenticated and configured topic metadata is accessible. No consumer joined, messages fetched or offsets changed; consume permission is not verified.");
                }, token);
            }, cancellationToken);
    }
}

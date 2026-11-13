using System.Text.Json;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class SqsSourceConnectionTester : ISourceConnectionTester
    {
        private readonly ISecretResolver _secrets;
        private readonly Sources.ISourceTransportClientFactory _clients;

        public SqsSourceConnectionTester(ISecretResolver secrets, Sources.ISourceTransportClientFactory? clients = null)
        {
            _secrets = secrets;
            _clients = clients ?? new Sources.SourceTransportClientFactory();
        }

        public SourceType Type => SourceType.SQS;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken) =>
            ConnectionProbe.RunAsync(async token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                var config = document.RootElement;
                var credentials = await ConnectionProbeCredentials.SourceAwsAsync(config, _secrets, token);
                if (credentials.IsFailure) return Result<string>.Fail(credentials.Errors);
                using var client = _clients.CreateSqs(config, credentials.Value);
                await client.GetQueueAttributesAsync(new GetQueueAttributesRequest
                {
                    QueueUrl = config.GetProperty("queueUrl").GetString()!,
                    AttributeNames = new List<string> { "QueueArn" }
                }, token);
                return Result.Success("SQS queue attributes are accessible with the configured credentials. No messages received or deleted; receive permission is not verified.");
            }, cancellationToken);
    }
}

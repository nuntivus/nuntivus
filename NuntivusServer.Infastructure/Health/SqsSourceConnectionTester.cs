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

        public SqsSourceConnectionTester(ISecretResolver secrets) => _secrets = secrets;

        public SourceType Type => SourceType.SQS;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken) =>
            ConnectionProbe.RunAsync(async token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                var config = document.RootElement;
                var credentials = await ConnectionProbeCredentials.SourceAwsAsync(config, _secrets, token);
                if (credentials.IsFailure) return Result<string>.Fail(credentials.Errors);
                var region = RegionEndpoint.GetBySystemName(config.GetProperty("region").GetString()!);
                using var client = credentials.Value is null
                    ? new AmazonSQSClient(region) : new AmazonSQSClient(credentials.Value, region);
                await client.GetQueueAttributesAsync(new GetQueueAttributesRequest
                {
                    QueueUrl = config.GetProperty("queueUrl").GetString()!,
                    AttributeNames = new List<string> { "QueueArn" }
                }, token);
                return Result.Success("SQS queue attributes are accessible with the configured credentials. No messages received or deleted; receive permission is not verified.");
            }, cancellationToken);
    }
}

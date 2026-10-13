using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class SqsDestinationConnectionTester : IDestinationConnectionTester
    {
        private readonly ISecretResolver _secrets;

        public SqsDestinationConnectionTester(ISecretResolver secrets) => _secrets = secrets;

        public DestinationType Type => DestinationType.Sqs;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken) =>
            ConnectionProbe.RunAsync(async token =>
            {
                var config = JsonSerializer.Deserialize<SqsDestinationConfig>(destination.Configuration)!;
                var secret = await ConnectionProbeCredentials.DestinationAsync(config.Credentials?.SecretAccessKey, _secrets, token);
                if (secret.IsFailure) return Result<string>.Fail(secret.Errors);
                var session = await ConnectionProbeCredentials.DestinationAsync(config.Credentials?.SessionToken, _secrets, token);
                if (session.IsFailure) return Result<string>.Fail(session.Errors);
                AWSCredentials? credentials = config.Credentials?.AccessKeyId is { } key && secret.Value is { } value
                    ? session.Value is { } sessionToken ? new SessionAWSCredentials(key, value, sessionToken)
                        : new BasicAWSCredentials(key, value)
                    : null;
                var options = new AmazonSQSConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(config.Region) };
                if (config.ServiceUrl is not null) options.ServiceURL = config.ServiceUrl;
                using var client = credentials is null ? new AmazonSQSClient(options) : new AmazonSQSClient(credentials, options);
                await client.GetQueueAttributesAsync(new GetQueueAttributesRequest
                {
                    QueueUrl = config.QueueUrl,
                    AttributeNames = new List<string> { "QueueArn" }
                }, token);
                return Result.Success("SQS queue is accessible with the configured credentials.");
            }, cancellationToken);
    }
}

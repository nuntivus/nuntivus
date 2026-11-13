using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Confluent.Kafka;
using NuntivusServer.Application.Security;
using NuntivusServer.Infastructure.Http;

namespace NuntivusServer.Infastructure.Sources
{
    public class SourceTransportClientFactory : ISourceTransportClientFactory
    {
        public virtual IAmazonSQS CreateSqs(JsonElement configuration, AWSCredentials? credentials)
        {
            var region = RegionEndpoint.GetBySystemName(configuration.GetProperty("region").GetString()!);
            return credentials is null ? new AmazonSQSClient(region) : new AmazonSQSClient(credentials, region);
        }

        public virtual IAmazonS3 CreateS3(JsonElement configuration, AWSCredentials? credentials)
        {
            var region = RegionEndpoint.GetBySystemName(configuration.GetProperty("region").GetString()!);
            return credentials is null ? new AmazonS3Client(region) : new AmazonS3Client(credentials, region);
        }

        public virtual AdminClientConfig CreateKafkaAdmin(JsonElement configuration, string? secret)
        {
            var settings = new AdminClientConfig(KafkaSettings(configuration, secret))
            {
                SocketTimeoutMs = 5000,
                AllowAutoCreateTopics = false
            };
            return settings;
        }

        public virtual ConsumerConfig CreateKafkaConsumer(JsonElement configuration, string? secret, string groupId) =>
            new(KafkaSettings(configuration, secret))
            {
                GroupId = groupId,
                EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

        private static Confluent.Kafka.ClientConfig KafkaSettings(JsonElement configuration, string? secret)
        {
            var settings = new Confluent.Kafka.ClientConfig
            {
                BootstrapServers = configuration.GetProperty("bootstrapServers").GetString(),
                SecurityProtocol = secret is null ? SecurityProtocol.Ssl : SecurityProtocol.SaslSsl
            };
            if (secret is not null)
            {
                settings.SaslMechanism = SaslMechanism.Plain;
                settings.SaslUsername = configuration.GetProperty("username").GetString();
                settings.SaslPassword = secret;
            }
            return settings;
        }

        public virtual HttpClient CreatePollingHttpClient(Uri endpoint)
        {
            var policy = new EndpointSecurityPolicy();
            var context = new EndpointSecurityContext(EndpointDirection.Source, "http-polling", "events.publish");
            var security = new EndpointSecurityOption { AllowHttpScheme = false };
            policy.ValidateEndpointUri(endpoint, context, security).EnsureSuccess();
            return new HttpClient(new ValidatedHttpConnection(policy, context, security).CreateHandler())
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }
    }
}

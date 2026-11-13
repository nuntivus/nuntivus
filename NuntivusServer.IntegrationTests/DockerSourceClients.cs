using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Confluent.Kafka;
using NuntivusServer.Infastructure.Sources;

namespace NuntivusServer.IntegrationTests
{
    internal sealed class DockerSourceClients(DestinationAdaptersFixture fixture) : SourceTransportClientFactory, IHttpClientFactory
    {
        public override IAmazonSQS CreateSqs(JsonElement configuration, AWSCredentials? credentials) =>
            new AmazonSQSClient(credentials ?? new BasicAWSCredentials("test", "test"),
                new AmazonSQSConfig { ServiceURL = fixture.LocalStackUrl, AuthenticationRegion = "us-east-1" });

        public override IAmazonS3 CreateS3(JsonElement configuration, AWSCredentials? credentials) =>
            new AmazonS3Client(credentials ?? new BasicAWSCredentials("test", "test"),
                new AmazonS3Config { ServiceURL = fixture.LocalStackUrl, AuthenticationRegion = "us-east-1", ForcePathStyle = true });

        public override AdminClientConfig CreateKafkaAdmin(JsonElement configuration, string? secret)
        {
            var settings = base.CreateKafkaAdmin(configuration, secret);
            settings.SecurityProtocol = SecurityProtocol.Plaintext;
            return settings;
        }

        public override ConsumerConfig CreateKafkaConsumer(JsonElement configuration, string? secret, string groupId)
        {
            var settings = base.CreateKafkaConsumer(configuration, secret, groupId);
            settings.SecurityProtocol = SecurityProtocol.Plaintext;
            return settings;
        }

        public override HttpClient CreatePollingHttpClient(Uri endpoint) => CreateClient("polling");
        public HttpClient CreateClient(string name) => new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
    }
}

using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Confluent.Kafka;

namespace NuntivusServer.Infastructure.Sources
{
    public interface ISourceTransportClientFactory
    {
        IAmazonSQS CreateSqs(JsonElement configuration, AWSCredentials? credentials);
        IAmazonS3 CreateS3(JsonElement configuration, AWSCredentials? credentials);
        AdminClientConfig CreateKafkaAdmin(JsonElement configuration, string? secret);
        ConsumerConfig CreateKafkaConsumer(JsonElement configuration, string? secret, string groupId);
        HttpClient CreatePollingHttpClient(Uri endpoint);
    }
}

using NuntivusServer.Application.Sources;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.UnitTests.Sources
{
    public sealed class PullSourceConfigurationHandlerTests
    {
        [Theory]
        [InlineData(SourceType.RABBITMQ, "{\"uri\":\"amqps://broker.example.com\",\"queue\":\"orders\"}")]
        [InlineData(SourceType.KAFKA, "{\"bootstrapServers\":\"broker:9093\",\"topic\":\"orders\",\"groupId\":\"ingestion\"}")]
        [InlineData(SourceType.SQS, "{\"region\":\"us-east-1\",\"queueUrl\":\"https://sqs.us-east-1.amazonaws.com/123/orders\"}")]
        [InlineData(SourceType.NATS, "{\"url\":\"tls://broker.example.com:4222\",\"stream\":\"EXTERNAL\",\"subject\":\"orders\",\"durable\":\"orders-consumer\"}")]
        [InlineData(SourceType.HTTP_POLLING, "{\"url\":\"https://example.com/events\"}")]
        [InlineData(SourceType.S3, "{\"region\":\"us-east-1\",\"bucket\":\"orders\",\"prefix\":\"events/\"}")]
        public void AcceptsMinimumConfiguration(SourceType type, string json)
        {
            Assert.True(new PullSourceConfigurationHandler(type).Normalize(json).IsSuccess);
        }

        [Theory]
        [InlineData("{\"url\":\"http://example.com/events\"}")]
        [InlineData("{\"url\":\"https://user:password@example.com/events\"}")]
        [InlineData("{\"url\":\"https://example.com/events\",\"password\":\"inline\"}")]
        [InlineData("{\"url\":\"https://example.com/events\",\"secretRef\":\"not-a-secret\"}")]
        public void RejectsUnsafePollingConfiguration(string json)
        {
            Assert.True(new PullSourceConfigurationHandler(SourceType.HTTP_POLLING).Normalize(json).IsFailure);
        }
    }
}

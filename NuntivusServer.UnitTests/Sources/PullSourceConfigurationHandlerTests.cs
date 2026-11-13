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

        [Fact]
        public void AcceptsPostPollingWithHeadersAndJsonBody()
        {
            var result = new PullSourceConfigurationHandler(SourceType.HTTP_POLLING).Normalize(
                "{\"url\":\"https://example.com/events\",\"method\":\"POST\",\"headers\":{\"X-Client\":\"test\"},\"body\":{\"page\":1}}");
            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData("\"method\":\"DELETE\"")]
        [InlineData("\"method\":\"GET\",\"body\":{}")]
        [InlineData("\"headers\":{\"Authorization\":\"Bearer secret\"}")]
        [InlineData("\"headers\":{\"Host\":\"internal\"}")]
        [InlineData("\"headers\":{\"X-Client\":42}")]
        public void RejectsUnsafePollingRequestSettings(string fields)
        {
            Assert.True(new PullSourceConfigurationHandler(SourceType.HTTP_POLLING).Normalize(
                $"{{\"url\":\"https://example.com/events\",{fields}}}").IsFailure);
        }

        public static IEnumerable<object[]> PullConfigurations()
        {
            yield return [SourceType.RABBITMQ, "{\"uri\":\"amqps://broker.example.com\",\"queue\":\"orders\"}"];
            yield return [SourceType.KAFKA, "{\"bootstrapServers\":\"broker:9093\",\"topic\":\"orders\",\"groupId\":\"ingestion\"}"];
            yield return [SourceType.SQS, "{\"region\":\"us-east-1\",\"queueUrl\":\"https://sqs.us-east-1.amazonaws.com/123/orders\"}"];
            yield return [SourceType.NATS, "{\"url\":\"tls://broker.example.com:4222\",\"stream\":\"EXTERNAL\",\"subject\":\"orders\",\"durable\":\"orders-consumer\"}"];
            yield return [SourceType.HTTP_POLLING, "{\"url\":\"https://example.com/events\"}"];
            yield return [SourceType.FOLDER, System.Text.Json.JsonSerializer.Serialize(new { path = Path.GetTempPath() })];
            yield return [SourceType.S3, "{\"region\":\"us-east-1\",\"bucket\":\"orders\",\"prefix\":\"events/\"}"];
        }

        [Theory]
        [MemberData(nameof(PullConfigurations))]
        public void Rejects_each_missing_required_field_and_inline_secrets(SourceType type, string json)
        {
            var handler = new PullSourceConfigurationHandler(type);
            var fields = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            Assert.True(handler.Normalize(json).IsSuccess);
            foreach (var key in fields.Select(field => field.Key).ToArray())
            {
                var missing = fields.DeepClone().AsObject();
                missing.Remove(key);
                Assert.True(handler.Normalize(missing.ToJsonString()).IsFailure, $"{type}: {key}");
                var wrongType = fields.DeepClone().AsObject();
                wrongType[key] = 42;
                Assert.True(handler.Normalize(wrongType.ToJsonString()).IsFailure, $"{type}: numeric {key}");
            }
            fields["password"] = "never-store-inline";
            Assert.True(handler.Normalize(fields.ToJsonString()).IsFailure);
            Assert.True(handler.Normalize("[]").IsFailure);
            Assert.True(handler.Normalize("invalid-json").IsFailure);
        }
    }
}

using System.Text.Json;
using Amazon.S3.Model;
using Amazon.SQS.Model;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;
using NuntivusServer.Infastructure.Health;
using NuntivusServer.Infastructure.Sources;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(DestinationAdaptersCollection))]
    public sealed class LiveSourceConnectivityTests(DestinationAdaptersFixture fixture)
    {
        internal sealed class Secrets : ISecretResolver
        {
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success("test"));
        }

        [Fact]
        public async Task Kafka_probe_reads_existing_topic_without_consuming_or_creating_topics()
        {
            var topic = $"source-probe-{Guid.NewGuid():N}";
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = fixture.KafkaBootstrapServers }).Build();
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
            using var producer = new ProducerBuilder<Null, string>(new ProducerConfig { BootstrapServers = fixture.KafkaBootstrapServers }).Build();
            await producer.ProduceAsync(topic, new Message<Null, string> { Value = "untouched" });
            var tester = new KafkaSourceConnectionTester(new Secrets(), new DockerSourceClients(fixture));
            var result = await tester.TestAsync(JsonSerializer.Serialize(new { bootstrapServers = fixture.KafkaBootstrapServers, topic }), CancellationToken.None);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Message : null);
            var missing = $"missing-{Guid.NewGuid():N}";
            Assert.True((await tester.TestAsync(JsonSerializer.Serialize(new { bootstrapServers = fixture.KafkaBootstrapServers, topic = missing }), CancellationToken.None)).IsFailure);
            Assert.DoesNotContain(admin.GetMetadata(TimeSpan.FromSeconds(5)).Topics, t => t.Topic == missing);
            using var consumer = new ConsumerBuilder<Ignore, string>(new ConsumerConfig
            {
                BootstrapServers = fixture.KafkaBootstrapServers, GroupId = Guid.NewGuid().ToString(), EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Earliest
            }).Build();
            consumer.Subscribe(topic);
            Assert.Equal("untouched", consumer.Consume(TimeSpan.FromSeconds(20))?.Message.Value);
            consumer.Close();
        }

        [Fact]
        public async Task Sqs_probe_leaves_seeded_message_available()
        {
            var clients = new DockerSourceClients(fixture);
            using var config = JsonDocument.Parse(JsonSerializer.Serialize(new { region = "us-east-1", queueUrl = fixture.SqsQueueUrl, accessKeyId = "test", secretRef = "secret.emulator" }));
            using var client = clients.CreateSqs(config.RootElement, null);
            var queue = (await client.CreateQueueAsync($"source-probe-{Guid.NewGuid():N}")).QueueUrl;
            try
            {
                await client.SendMessageAsync(queue, "untouched");
                var normalized = JsonSerializer.Serialize(new { region = "us-east-1", queueUrl = queue, accessKeyId = "test", secretRef = "secret.emulator" });
                var result = await new SqsSourceConnectionTester(new Secrets(), clients).TestAsync(normalized, CancellationToken.None);
                Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Message : null);
                var received = await client.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queue, WaitTimeSeconds = 1 });
                Assert.Equal("untouched", Assert.Single(received.Messages).Body);
            }
            finally { await client.DeleteQueueAsync(queue); }
        }

        [Fact]
        public async Task S3_probe_preserves_object_and_reports_missing_bucket()
        {
            var clients = new DockerSourceClients(fixture);
            var prefix = $"probe/{Guid.NewGuid():N}/";
            var normalized = JsonSerializer.Serialize(new { region = "us-east-1", bucket = fixture.S3Bucket, prefix, accessKeyId = "test", secretRef = "secret.emulator" });
            using var config = JsonDocument.Parse(normalized);
            using var client = clients.CreateS3(config.RootElement, null);
            var key = prefix + "event.json";
            await client.PutObjectAsync(new PutObjectRequest { BucketName = fixture.S3Bucket, Key = key, ContentBody = "untouched" });
            try
            {
                var tester = new S3SourceConnectionTester(new Secrets(), clients);
                var result = await tester.TestAsync(normalized, CancellationToken.None);
                Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Message : null);
                using var response = await client.GetObjectAsync(fixture.S3Bucket, key);
                using var reader = new StreamReader(response.ResponseStream);
                Assert.Equal("untouched", await reader.ReadToEndAsync());
                Assert.True((await tester.TestAsync(JsonSerializer.Serialize(new { region = "us-east-1", bucket = "missing-bucket", prefix = "" }), CancellationToken.None)).IsFailure);
            }
            finally { await client.DeleteObjectAsync(fixture.S3Bucket, key); }
        }

        [Fact]
        public async Task Http_polling_probe_uses_live_head_and_inbound_http_validates_credentials()
        {
            var tester = new HttpPollingSourceConnectionTester(new DockerSourceClients(fixture),
                new HttpDestinationSecurityOption { AllowHttpScheme = true, AllowPrivateNetworkDestinations = true }, new Secrets());
            var result = await tester.TestAsync(JsonSerializer.Serialize(new { url = fixture.HttpUrl }), CancellationToken.None);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Message : null);
            Assert.True((await new HttpApiSourceConnectionTester(new Secrets()).TestAsync(
                "{\"authentication\":{\"type\":\"token\",\"secretRef\":\"secret.emulator\"}}", CancellationToken.None)).IsSuccess);
        }

        [Fact]
        public void Default_factory_keeps_tls_and_blocks_private_polling_endpoints()
        {
            using var config = JsonDocument.Parse("{\"bootstrapServers\":\"localhost:9092\",\"username\":\"test\"}");
            var clients = new SourceTransportClientFactory();
            Assert.Equal(SecurityProtocol.Ssl, clients.CreateKafkaAdmin(config.RootElement, null).SecurityProtocol);
            Assert.Equal(SecurityProtocol.SaslSsl, clients.CreateKafkaConsumer(config.RootElement, "secret", "group").SecurityProtocol);
            Assert.ThrowsAny<Exception>(() => clients.CreatePollingHttpClient(new Uri("http://127.0.0.1/events")));
        }
    }
}

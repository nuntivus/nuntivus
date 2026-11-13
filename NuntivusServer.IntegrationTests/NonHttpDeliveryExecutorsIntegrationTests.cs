using Confluent.Kafka;
using Confluent.Kafka.Admin;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Infastructure.Deliveries.Kafka;
using NuntivusServer.Infastructure.Deliveries.Nats;
using NuntivusServer.Infastructure.Deliveries.RabbitMq;
using NuntivusServer.Infastructure.Deliveries.Sqs;
using NuntivusServer.Infastructure.Health;
using RabbitMQ.Client;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DomainDestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(DestinationAdaptersCollection))]
    public sealed class NonHttpDeliveryExecutorsIntegrationTests
    {
        private readonly DestinationAdaptersFixture _fx;

        public NonHttpDeliveryExecutorsIntegrationTests(DestinationAdaptersFixture fx)
        {
            _fx = fx;
        }

        [Fact]
        public async Task Kafka_executor_publishes_successfully()
        {
            var topic = $"nuntivus.delivery.{Guid.NewGuid():N}";
            using (var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _fx.KafkaBootstrapServers,
            }).Build())
            {
                await admin.CreateTopicsAsync(new[] { new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 } });
            }

            var executor = new KafkaDeliveryExecutor(new NoopSecretResolver(), new NullAppLogger<KafkaDeliveryExecutor>());
            var request = BuildRequest(
                DomainDestinationType.Kafka,
                new KafkaDestinationConfig(_fx.KafkaBootstrapServers, topic, TimeoutSeconds: 15));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.True(result.Succeeded, result.Error);
            Assert.NotNull(result.Metadata);
            Assert.Equal(topic, result.Metadata!["topic"]);
            var test = await new KafkaDestinationConnectionTester(executor).TestAsync(request.Destination,
                "{\"test\":\"kafka\"}", CancellationToken.None);
            Assert.True(test.IsSuccess, test.IsFailure ? test.Errors[0].Message : null);
        }

        [Fact]
        public void RabbitMq_exclusive_queue_rejects_another_connection()
        {
            var factory = new ConnectionFactory { Uri = new Uri(_fx.RabbitMqConnectionString) };
            using var owner = factory.CreateConnection();
            using var ownerChannel = owner.CreateModel();
            var queue = ownerChannel.QueueDeclare().QueueName;
            using var probe = factory.CreateConnection();
            using var probeChannel = probe.CreateModel();
            var error = Assert.Throws<RabbitMQ.Client.Exceptions.OperationInterruptedException>(() =>
                probeChannel.QueueDeclarePassive(queue));
            Assert.Equal(405, error.ShutdownReason.ReplyCode);
        }

        [Fact]
        public async Task RabbitMq_source_test_leaves_messages_untouched()
        {
            var factory = new ConnectionFactory { Uri = new Uri(_fx.RabbitMqConnectionString) };
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();
            var queue = channel.QueueDeclare($"source-probe-{Guid.NewGuid():N}", durable: false,
                exclusive: false, autoDelete: false).QueueName;
            try
            {
                channel.BasicPublish("", queue, null, System.Text.Encoding.UTF8.GetBytes("{\"test\":true}"));
                var tester = new RabbitMqSourceConnectionTester(new NoopSecretResolver());
                var config = JsonSerializer.Serialize(new { uri = _fx.RabbitMqConnectionString, queue });
                var result = await tester.TestAsync(config, CancellationToken.None);
                Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Message : null);
                var state = channel.QueueDeclarePassive(queue);
                Assert.Equal(1u, state.MessageCount);
                Assert.Equal(0u, state.ConsumerCount);
            }
            finally { channel.QueueDelete(queue); }
        }

        [Fact]
        public async Task Nats_executor_publishes_successfully_with_jetstream_ack()
        {
            var executor = new NatsDeliveryExecutor(new NoopSecretResolver(), new NullAppLogger<NatsDeliveryExecutor>());
            var request = BuildRequest(
                DomainDestinationType.Nats,
                new NatsDestinationConfig(
                    _fx.NatsUrl,
                    Subject: $"delivery.nats.{Guid.NewGuid():N}",
                    TimeoutSeconds: 15,
                    UseJetStream: true));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.True(result.Succeeded, result.Error);
            Assert.NotNull(result.Metadata);
            Assert.Equal("NUNTIVUS_DELIVERY_IT", result.Metadata!["stream"]);
            var test = await new NatsDestinationConnectionTester(executor).TestAsync(request.Destination,
                "{\"test\":\"nats\"}", CancellationToken.None);
            Assert.True(test.IsSuccess, test.IsFailure ? test.Errors[0].Message : null);
            var sourceTest = await new NatsSourceConnectionTester(new NoopSecretResolver()).TestAsync(
                JsonSerializer.Serialize(new { url = _fx.NatsUrl, stream = "NUNTIVUS_DELIVERY_IT", subject = "delivery.>", durable = "test" }), CancellationToken.None);
            Assert.True(sourceTest.IsSuccess, sourceTest.IsFailure ? sourceTest.Errors[0].Message : null);
        }

        [Fact]
        public async Task RabbitMq_executor_publishes_successfully_with_broker_confirm()
        {
            var exchange = $"nuntivus.delivery.{Guid.NewGuid():N}";
            var routingKey = $"rk.{Guid.NewGuid():N}";

            var factory = new ConnectionFactory { Uri = new Uri(_fx.RabbitMqConnectionString) };
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                channel.ExchangeDeclare(exchange, ExchangeType.Direct, durable: false, autoDelete: false);
                var queue = channel.QueueDeclare().QueueName;
                channel.QueueBind(queue, exchange, routingKey);
            }

            var executor = new RabbitMqDeliveryExecutor(new NoopSecretResolver(), new NullAppLogger<RabbitMqDeliveryExecutor>());
            var request = BuildRequest(
                DomainDestinationType.RabbitMq,
                new RabbitMqDestinationConfig(
                    _fx.RabbitMqConnectionString,
                    exchange,
                    routingKey,
                    Persistent: false,
                    TimeoutSeconds: 15));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.True(result.Succeeded, result.Error);
            Assert.NotNull(result.Metadata);
            Assert.Equal("true", result.Metadata!["brokerAck"]);
            var test = await new RabbitMqDestinationConnectionTester(executor).TestAsync(request.Destination,
                "{\"test\":\"rabbitmq\"}", CancellationToken.None);
            Assert.True(test.IsSuccess, test.IsFailure ? test.Errors[0].Message : null);
        }

        [Fact]
        public async Task Sqs_executor_publishes_successfully()
        {
            var executor = new SqsDeliveryExecutor(new NoopSecretResolver(), new NullAppLogger<SqsDeliveryExecutor>());
            var request = BuildRequest(
                DomainDestinationType.Sqs,
                new SqsDestinationConfig(
                    Region: "us-east-1",
                    QueueUrl: _fx.SqsQueueUrl,
                    ServiceUrl: _fx.LocalStackUrl,
                    TimeoutSeconds: 15,
                    Credentials: new SqsCredentials(
                        AccessKeyId: "test",
                        SecretAccessKey: new DestinationCredential("inline", "test"))));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.True(result.Succeeded, result.Error);
            Assert.NotNull(result.Metadata);
            Assert.False(string.IsNullOrWhiteSpace(result.Metadata!["messageId"]));
            var test = await new SqsDestinationConnectionTester(executor).TestAsync(request.Destination,
                "{\"test\":\"sqs\"}", CancellationToken.None);
            Assert.True(test.IsSuccess, test.IsFailure ? test.Errors[0].Message : null);
        }

        private static DeliveryExecutionRequest BuildRequest(DomainDestinationType destinationType, object config)
        {
            return new DeliveryExecutionRequest(
                DeliveryId: Guid.NewGuid(),
                EventId: Guid.NewGuid(),
                ContractId: Guid.NewGuid(),
                CustomerId: "acme",
                EventType: "orders.created",
                Payload: "{\"orderId\":123}",
                AttemptNumber: 1,
                Destination: new Destination(destinationType, JsonSerializer.Serialize(config)));
        }

        private sealed class NoopSecretResolver : ISecretResolver
        {
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Failure<string>(NuntivusServer.Common.Error.Validation("Secret resolver not expected in these tests.")));
        }

        private sealed class NullAppLogger<T> : IAppLogger<T>
        {
            public void Debug(string m, params object?[] a) { }
            public void Information(string m, params object?[] a) { }
            public void Warning(string m, params object?[] a) { }
            public void Error(string m, params object?[] a) { }
            public void Error(Exception ex, string m, params object?[] a) { }
            public void Fatal(string m, params object?[] a) { }
            public void Fatal(Exception ex, string m, params object?[] a) { }
        }
    }
}

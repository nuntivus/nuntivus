using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Confluent.Kafka;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Domain.Sources;
using RabbitMQ.Client;

namespace NuntivusServer.Worker.Sources
{
    public sealed class BrokerSourceRunner : ISourceRunner
    {
        private readonly ISourceEventProcessor _processor;
        private readonly ISecretResolver _secrets;
        private readonly NuntivusServer.Infastructure.Sources.ISourceTransportClientFactory _clients;

        public BrokerSourceRunner(SourceType type, ISourceEventProcessor processor, ISecretResolver secrets,
            NuntivusServer.Infastructure.Sources.ISourceTransportClientFactory? clients = null)
        {
            Type = type;
            _processor = processor;
            _secrets = secrets;
            _clients = clients ?? new NuntivusServer.Infastructure.Sources.SourceTransportClientFactory();
        }

        public SourceType Type { get; }

        public async Task RunAsync(Source source, SourceLease lease, CancellationToken cancellationToken)
        {
            using var config = JsonDocument.Parse(source.Configuration);
            var root = config.RootElement;
            var secret = root.TryGetProperty("secretRef", out var reference)
                ? (await _secrets.ResolveAsync(reference.GetString()!, cancellationToken)).EnsureValue() : null;
            switch (Type)
            {
                case SourceType.RABBITMQ:
                    await RunRabbitAsync(root, lease, secret, cancellationToken);
                    break;
                case SourceType.KAFKA:
                    await RunKafkaAsync(root, lease, secret, cancellationToken);
                    break;
                case SourceType.SQS:
                    await RunSqsAsync(root, lease, secret, cancellationToken);
                    break;
                case SourceType.NATS:
                    await RunNatsAsync(root, lease, secret, cancellationToken);
                    break;
            }
        }

        private async Task RunRabbitAsync(JsonElement config, SourceLease lease, string? secret, CancellationToken token)
        {
            var uri = new Uri(config.GetProperty("uri").GetString()!);
            var factory = new ConnectionFactory { Uri = uri, DispatchConsumersAsync = false };
            if (config.TryGetProperty("username", out var user)) factory.UserName = user.GetString();
            if (secret is not null) factory.Password = secret;
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();
            channel.BasicQos(0, 1, false);
            var queue = config.GetProperty("queue").GetString()!;
            var retries = 0;
            while (!token.IsCancellationRequested)
            {
                var message = channel.BasicGet(queue, autoAck: false);
                if (message is null) { await Task.Delay(500, token); continue; }
                var key = message.BasicProperties.MessageId;
                if (string.IsNullOrWhiteSpace(key))
                {
                    channel.BasicNack(message.DeliveryTag, false, true);
                    await Task.Delay(1000, token);
                    continue;
                }
                var result = await _processor.ProcessAsync(Type, lease, Encoding.UTF8.GetString(message.Body.ToArray()), key, token);
                if (result.IsSuccess) { channel.BasicAck(message.DeliveryTag, false); retries = 0; }
                else
                {
                    channel.BasicNack(message.DeliveryTag, false, true);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(retries++, 5))), token);
                }
            }
        }

        private async Task RunKafkaAsync(JsonElement config, SourceLease lease, string? secret, CancellationToken token)
        {
            var settings = _clients.CreateKafkaConsumer(config, secret,
                $"{config.GetProperty("groupId").GetString()}-{lease.SourceId}");
            using var consumer = new ConsumerBuilder<Ignore, string>(settings).Build();
            consumer.Subscribe(config.GetProperty("topic").GetString());
            var retries = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var record = consumer.Consume(token);
                    var key = $"{record.Topic}/{record.Partition.Value}/{record.Offset.Value}";
                    var result = await _processor.ProcessAsync(Type, lease, record.Message.Value, key, token);
                    if (result.IsSuccess) { consumer.Commit(record); retries = 0; }
                    else
                    {
                        consumer.Seek(record.TopicPartitionOffset);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(retries++, 5))), token);
                    }
                }
            }
            finally { consumer.Close(); }
        }

        private async Task RunSqsAsync(JsonElement config, SourceLease lease, string? secret, CancellationToken token)
        {
            using var client = _clients.CreateSqs(config,
                secret is not null && config.TryGetProperty("accessKeyId", out var accessKey)
                    ? new BasicAWSCredentials(accessKey.GetString(), secret) : null);
            var queue = config.GetProperty("queueUrl").GetString();
            while (!token.IsCancellationRequested)
            {
                var response = await client.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queue, MaxNumberOfMessages = 1, WaitTimeSeconds = 10, VisibilityTimeout = 60
                }, token);
                foreach (var message in response.Messages)
                {
                    var result = await _processor.ProcessAsync(Type, lease, message.Body,
                        message.MessageId, token);
                    if (result.IsSuccess) await client.DeleteMessageAsync(queue, message.ReceiptHandle, token);
                }
            }
        }

        private async Task RunNatsAsync(JsonElement config, SourceLease lease, string? secret, CancellationToken token)
        {
            var options = NatsOpts.Default with
            {
                Url = config.GetProperty("url").GetString()!,
                AuthOpts = NatsAuthOpts.Default with { Token = secret }
            };
            await using var connection = new NatsConnection(options);
            var jetStream = new NatsJSContext(connection);
            var durable = $"{config.GetProperty("durable").GetString()}-{lease.SourceId.Replace("-", "")}";
            var consumer = await jetStream.CreateOrUpdateConsumerAsync(config.GetProperty("stream").GetString()!,
                new NATS.Client.JetStream.Models.ConsumerConfig(durable)
                {
                    FilterSubject = config.GetProperty("subject").GetString()!,
                    AckPolicy = ConsumerConfigAckPolicy.Explicit,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                    AckWait = TimeSpan.FromSeconds(60),
                    MaxDeliver = -1
                }, token);
            while (!token.IsCancellationRequested)
            {
                await foreach (var message in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts
                    { MaxMsgs = 1, Expires = TimeSpan.FromSeconds(5) }, cancellationToken: token))
                {
                    var key = message.Metadata?.Sequence.Stream.ToString() ?? string.Empty;
                    var result = await _processor.ProcessAsync(Type, lease,
                        Encoding.UTF8.GetString(message.Data ?? Array.Empty<byte>()), key, token);
                    if (result.IsSuccess) await message.AckAsync(cancellationToken: token);
                    else await message.NakAsync(delay: TimeSpan.FromSeconds(5), cancellationToken: token);
                }
            }
        }
    }
}

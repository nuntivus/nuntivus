using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Amazon.S3.Model;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Configuration;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Domain.Sources;
using NuntivusServer.Infastructure.Deliveries.Http;
using NuntivusServer.Infastructure.Deliveries.Kafka;
using NuntivusServer.Infastructure.Deliveries.Nats;
using NuntivusServer.Infastructure.Deliveries.RabbitMq;
using NuntivusServer.Infastructure.Deliveries.Sqs;
using NuntivusServer.Infastructure.Health;
using NuntivusServer.Worker.Sources;
using RabbitMQ.Client;
using Xunit.Abstractions;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(DestinationAdaptersCollection))]
    [Trait("Category", "AdapterStress")]
    public sealed class AdapterStressTests(DestinationAdaptersFixture fixture, ITestOutputHelper output)
    {
        private static int Samples => int.TryParse(Environment.GetEnvironmentVariable("STRESS_SAMPLES"), out var count)
            ? Math.Clamp(count, 1, 100_000) : 10;

        private sealed class Logger<T> : IAppLogger<T>
        {
            public void Debug(string m, params object?[] a) { }
            public void Information(string m, params object?[] a) { }
            public void Warning(string m, params object?[] a) { }
            public void Error(string m, params object?[] a) { }
            public void Error(Exception ex, string m, params object?[] a) { }
            public void Fatal(string m, params object?[] a) { }
            public void Fatal(Exception ex, string m, params object?[] a) { }
        }

        private sealed class Measurement
        {
            private readonly Process _process = Process.GetCurrentProcess();
            private readonly TimeSpan _cpu = Process.GetCurrentProcess().TotalProcessorTime;
            private readonly long _allocated = GC.GetTotalAllocatedBytes();
            private readonly int[] _gc = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
            public Stopwatch Clock { get; } = Stopwatch.StartNew();
            public ConcurrentBag<double> Latencies { get; } = [];
            public ConcurrentBag<string> Errors { get; } = [];
            public void Save(string name, int requested, int concurrency, string latencyKind, ITestOutputHelper output)
            {
                Clock.Stop();
                _process.Refresh();
                var times = Latencies.Order().ToArray();
                double Percentile(double percentile) => times.Length == 0 ? 0 : times[Math.Max(0, (int)Math.Ceiling(times.Length * percentile) - 1)];
                var result = new
                {
                    name, requested, completed = times.Length, errors = Errors.Count, concurrency, latencyKind,
                    elapsedSeconds = Clock.Elapsed.TotalSeconds, operationsPerSecond = times.Length / Clock.Elapsed.TotalSeconds,
                    p50Ms = Percentile(.50), p95Ms = Percentile(.95), p99Ms = Percentile(.99), maxMs = Percentile(1),
                    processCpuMs = (_process.TotalProcessorTime - _cpu).TotalMilliseconds,
                    allocatedBytes = GC.GetTotalAllocatedBytes() - _allocated,
                    workingSetBytes = _process.WorkingSet64, peakWorkingSetBytes = _process.PeakWorkingSet64,
                    gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - _gc[i]).ToArray(),
                    runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    measuredAtUtc = DateTimeOffset.UtcNow, errorExamples = Errors.Take(3).ToArray()
                };
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                output.WriteLine(json);
                var directory = Environment.GetEnvironmentVariable("PERF_RESULTS_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, $"{name}-{concurrency}.json"), json);
                }
            }
        }

        [Theory]
        [InlineData(DestinationType.Http, 1)]
        [InlineData(DestinationType.Http, 8)]
        [InlineData(DestinationType.Kafka, 1)]
        [InlineData(DestinationType.Kafka, 8)]
        [InlineData(DestinationType.Nats, 1)]
        [InlineData(DestinationType.Nats, 8)]
        [InlineData(DestinationType.RabbitMq, 1)]
        [InlineData(DestinationType.RabbitMq, 8)]
        [InlineData(DestinationType.Sqs, 1)]
        [InlineData(DestinationType.Sqs, 8)]
        public async Task Destination_adapter_stress(DestinationType type, int concurrency)
        {
            var clients = new DockerSourceClients(fixture);
            var secrets = new LiveSourceConnectivityTests.Secrets();
            var topic = $"adapter-stress-{Guid.NewGuid():N}";
            object config;
            IDeliveryExecutor executor;
            switch (type)
            {
                case DestinationType.Http:
                    config = new HttpDestinationConfig(fixture.HttpUrl, new HttpAuthentication("none"));
                    executor = new HttpDeliveryExecutor(clients, secrets, new Logger<HttpDeliveryExecutor>(),
                        new HttpDestinationSecurityOption { AllowPrivateNetworkDestinations = true });
                    break;
                case DestinationType.Kafka:
                    using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = fixture.KafkaBootstrapServers }).Build())
                        await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
                    config = new KafkaDestinationConfig(fixture.KafkaBootstrapServers, topic, TimeoutSeconds: 15);
                    executor = new KafkaDeliveryExecutor(secrets, new Logger<KafkaDeliveryExecutor>());
                    break;
                case DestinationType.Nats:
                    config = new NatsDestinationConfig(fixture.NatsUrl, $"delivery.nats.{topic}", UseJetStream: true);
                    executor = new NatsDeliveryExecutor(secrets, new Logger<NatsDeliveryExecutor>());
                    break;
                case DestinationType.RabbitMq:
                    using (var connection = new ConnectionFactory { Uri = new Uri(fixture.RabbitMqConnectionString) }.CreateConnection())
                    using (var channel = connection.CreateModel()) channel.ExchangeDeclare(topic, ExchangeType.Fanout);
                    config = new RabbitMqDestinationConfig(fixture.RabbitMqConnectionString, topic, "stress");
                    executor = new RabbitMqDeliveryExecutor(secrets, new Logger<RabbitMqDeliveryExecutor>());
                    break;
                default:
                    config = new SqsDestinationConfig("us-east-1", fixture.SqsQueueUrl, ServiceUrl: fixture.LocalStackUrl,
                        Credentials: new SqsCredentials("test", new DestinationCredential("inline", "test")));
                    executor = new SqsDeliveryExecutor(secrets, new Logger<SqsDeliveryExecutor>());
                    break;
            }
            var destination = new Destination(type, JsonSerializer.Serialize(config));
            DeliveryExecutionRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "stress", "stress.created",
                JsonSerializer.Serialize(new { data = new string('x', 1024) }), 1, destination);
            for (var i = 0; i < 5; i++) Assert.True((await executor.ExecuteAsync(Request(), CancellationToken.None)).Succeeded);
            if (executor is HttpDeliveryExecutor http)
                Assert.True((await new HttpDestinationConnectionTester(http).TestAsync(destination, CancellationToken.None)).IsSuccess);
            var measurement = new Measurement();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await Parallel.ForEachAsync(Enumerable.Range(0, Samples), new ParallelOptions
                { MaxDegreeOfParallelism = concurrency, CancellationToken = timeout.Token }, async (_, token) =>
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var result = await executor.ExecuteAsync(Request(), token);
                    if (result.Succeeded) measurement.Latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    else measurement.Errors.Add(result.Error ?? result.Category.ToString());
                }
                catch (Exception ex) { measurement.Errors.Add(ex.GetType().Name); }
            });
            measurement.Save($"destination-{type}", Samples, concurrency, "single-attempt incl serialization, excludes warmup", output);
            Assert.Empty(measurement.Errors);
            Assert.Equal(Samples, measurement.Latencies.Count);
        }

        [Theory]
        [InlineData(SourceType.RABBITMQ)]
        [InlineData(SourceType.KAFKA)]
        [InlineData(SourceType.SQS)]
        [InlineData(SourceType.NATS)]
        [InlineData(SourceType.S3)]
        [InlineData(SourceType.FOLDER)]
        [InlineData(SourceType.HTTP_POLLING)]
        public async Task Source_adapter_stress(SourceType type)
        {
            var clients = new DockerSourceClients(fixture);
            var unique = Guid.NewGuid().ToString("N");
            var root = Path.Combine(Path.GetTempPath(), "nuntivus-stress-" + unique);
            var folder = Path.Combine(root, "incoming");
            Directory.CreateDirectory(folder);
            var prefix = $"stress/{unique}/";
            var body = JsonSerializer.Serialize(new { type = "stress.created", payload = new { data = new string('x', 1024) } });
            object config;
            using var awsConfig = JsonDocument.Parse("{\"region\":\"us-east-1\"}");
            using var sqs = clients.CreateSqs(awsConfig.RootElement, null);
            using var s3 = clients.CreateS3(awsConfig.RootElement, null);
            string? queue = null;
            try
            {
                switch (type)
                {
                    case SourceType.RABBITMQ:
                        using (var connection = new ConnectionFactory { Uri = new Uri(fixture.RabbitMqConnectionString) }.CreateConnection())
                        using (var channel = connection.CreateModel())
                        {
                            channel.QueueDeclare(unique, false, false, false);
                            for (var i = 0; i < Samples; i++)
                            {
                                var properties = channel.CreateBasicProperties(); properties.MessageId = i.ToString();
                                channel.BasicPublish("", unique, properties, Encoding.UTF8.GetBytes(body));
                            }
                        }
                        config = new { uri = fixture.RabbitMqConnectionString, queue = unique };
                        break;
                    case SourceType.KAFKA:
                        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = fixture.KafkaBootstrapServers }).Build())
                            await admin.CreateTopicsAsync([new TopicSpecification { Name = unique, NumPartitions = 1, ReplicationFactor = 1 }]);
                        using (var producer = new ProducerBuilder<Null, string>(new ProducerConfig { BootstrapServers = fixture.KafkaBootstrapServers }).Build())
                            for (var i = 0; i < Samples; i++) await producer.ProduceAsync(unique, new Message<Null, string> { Value = body });
                        config = new { bootstrapServers = fixture.KafkaBootstrapServers, topic = unique, groupId = unique };
                        break;
                    case SourceType.NATS:
                        await using (var connection = new NatsConnection(new NatsOpts { Url = fixture.NatsUrl }))
                        {
                            var js = new NatsJSContext(connection);
                            for (var i = 0; i < Samples; i++) await js.PublishAsync($"delivery.nats.{unique}", Encoding.UTF8.GetBytes(body));
                        }
                        config = new { url = fixture.NatsUrl, stream = "NUNTIVUS_DELIVERY_IT", subject = $"delivery.nats.{unique}", durable = unique };
                        break;
                    case SourceType.SQS:
                        queue = (await sqs.CreateQueueAsync(unique)).QueueUrl;
                        for (var i = 0; i < Samples; i++) await sqs.SendMessageAsync(queue, body);
                        config = new { region = "us-east-1", queueUrl = queue, accessKeyId = "test", secretRef = "secret.emulator" };
                        break;
                    case SourceType.S3:
                        for (var i = 0; i < Samples; i++) await s3.PutObjectAsync(new PutObjectRequest
                            { BucketName = fixture.S3Bucket, Key = $"{prefix}{i}.json", ContentBody = body });
                        config = new { region = "us-east-1", bucket = fixture.S3Bucket, prefix, intervalSeconds = 1, accessKeyId = "test", secretRef = "secret.emulator" };
                        break;
                    case SourceType.FOLDER:
                        for (var i = 0; i < Samples; i++) await File.WriteAllTextAsync(Path.Combine(folder, $"{i}.json"), body);
                        config = new { path = folder, intervalSeconds = 1 };
                        break;
                    default:
                        config = new { url = fixture.HttpUrl, intervalSeconds = 0 };
                        break;
                }
                var measurement = new Measurement();
                var processor = new Processor(Samples, measurement);
                ISourceRunner runner = type is SourceType.S3 or SourceType.FOLDER or SourceType.HTTP_POLLING
                    ? new ScanSourceRunner(type, processor, processor, new LiveSourceConnectivityTests.Secrets(),
                        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Sources:FolderRoot"] = root }).Build(), clients)
                    : new BrokerSourceRunner(type, processor, new LiveSourceConnectivityTests.Secrets(), clients);
                var source = Source.Create(unique, "Stress", type, JsonSerializer.Serialize(config), true);
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var run = Task.Run(async () =>
                {
                    try { await runner.RunAsync(source, new SourceLease(unique, "stress", 1), cancellation.Token); }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                });
                try
                {
                    var finished = await Task.WhenAny(processor.Completion.Task, run).WaitAsync(TimeSpan.FromMinutes(5));
                    if (finished == run) { await run; Assert.Fail("Source stopped before consuming all samples."); }
                    await processor.Completion.Task;
                    measurement.Clock.Stop();
                    await Task.Delay(100);
                }
                catch (Exception ex) { measurement.Errors.Add(ex.ToString()); }
                finally { cancellation.Cancel(); await run; }
                measurement.Save($"source-{type}", Samples, 1, "preseeded queue residence since runner start; cold connect included; in-memory processor", output);
                Assert.Empty(measurement.Errors);
                Assert.Equal(Samples, measurement.Latencies.Count);
            }
            finally
            {
                if (queue is not null) await sqs.DeleteQueueAsync(queue);
                if (type == SourceType.S3)
                    for (var i = 0; i < Samples; i++) await s3.DeleteObjectAsync(fixture.S3Bucket, $"{prefix}{i}.json");
                Directory.Delete(root, true);
            }
        }

        private sealed class Processor(int target, Measurement measurement) : ISourceEventProcessor, ISourceIngestionStateRepository
        {
            private readonly HashSet<string> _processed = [];
            public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<Result> ProcessAsync(SourceType type, SourceLease lease, string body, string stableKey, CancellationToken cancellationToken)
            {
                using var parsed = JsonDocument.Parse(body);
                Assert.Equal("stress.created", parsed.RootElement.GetProperty("type").GetString());
                if (_processed.Add(stableKey) && measurement.Latencies.Count < target)
                {
                    measurement.Latencies.Add(measurement.Clock.Elapsed.TotalMilliseconds);
                    if (measurement.Latencies.Count == target) Completion.TrySetResult();
                }
                return Task.FromResult(Result.Success());
            }
            public Task<Result<bool>> HasProcessedAsync(string sourceId, string itemKey, CancellationToken cancellationToken) => Task.FromResult(Result.Success(_processed.Contains(itemKey)));
            public Task<Result<SourceLease?>> TryAcquireAsync(string sourceId, string ownerId, TimeSpan duration, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<Result<bool>> RenewAsync(SourceLease lease, TimeSpan duration, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<Result> ReleaseAsync(SourceLease lease, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<Result<bool>> MarkProcessedAsync(SourceLease lease, string itemKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        }
    }
}

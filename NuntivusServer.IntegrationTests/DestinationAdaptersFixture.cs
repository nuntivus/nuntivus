using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Testcontainers.Kafka;
using Testcontainers.RabbitMq;

namespace NuntivusServer.IntegrationTests
{
    [CollectionDefinition(nameof(DestinationAdaptersCollection))]
    public sealed class DestinationAdaptersCollection : ICollectionFixture<DestinationAdaptersFixture> { }

    public sealed class DestinationAdaptersFixture : IAsyncLifetime
    {
        private readonly KafkaContainer _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.6.1")
            .Build();

        private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
            .WithImage("rabbitmq:3.13-alpine")
            .Build();

        private readonly IContainer _nats = new ContainerBuilder()
            .WithImage("nats:2.10-alpine")
            .WithCommand("-js")
            .WithPortBinding(4222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
            .Build();

        private readonly IContainer _localStack = new ContainerBuilder()
            .WithImage("localstack/localstack:3.5")
            .WithEnvironment("SERVICES", "sqs,s3")
            .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1")
            .WithEnvironment("AWS_ACCESS_KEY_ID", "test")
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", "test")
            .WithPortBinding(4566, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4566))
            .Build();

        private readonly IContainer _http = new ContainerBuilder()
            .WithImage("python:3.12-alpine")
            .WithEntrypoint("python", "-u", "-c")
            .WithCommand("from http.server import BaseHTTPRequestHandler,HTTPServer\nimport json\nclass Handler(BaseHTTPRequestHandler):\n sequence=0\n def log_message(self,*args): pass\n def do_HEAD(self):\n  self.send_response(200);self.end_headers()\n def do_GET(self):\n  Handler.sequence+=1\n  body=json.dumps({'type':'stress.created','payload':{'sequence':Handler.sequence}}).encode()\n  self.send_response(200);self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)\n def do_POST(self):\n  self.rfile.read(int(self.headers.get('Content-Length','0')));self.send_response(200);self.end_headers()\nHTTPServer(('0.0.0.0',8080),Handler).serve_forever()")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080))
            .Build();

        public string KafkaBootstrapServers => _kafka.GetBootstrapAddress();
        public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();
        public string NatsUrl => $"nats://{_nats.Hostname}:{_nats.GetMappedPublicPort(4222)}";
        public string LocalStackUrl => $"http://{_localStack.Hostname}:{_localStack.GetMappedPublicPort(4566)}";
        public string HttpUrl => $"http://{_http.Hostname}:{_http.GetMappedPublicPort(8080)}/";
        public string SqsQueueUrl { get; private set; } = string.Empty;
        public string S3Bucket => "nuntivus-source-it";

        public async Task InitializeAsync()
        {
            await _kafka.StartAsync();
            await _rabbitMq.StartAsync();
            await _nats.StartAsync();
            await _localStack.StartAsync();
            await _http.StartAsync();

            await EnsureNatsStreamAsync();
            await EnsureSqsQueueAsync();
            var bucket = await _localStack.ExecAsync(new[] { "awslocal", "s3api", "create-bucket", "--bucket", S3Bucket });
            if (bucket.ExitCode != 0) throw new InvalidOperationException(bucket.Stderr);
        }

        public async Task DisposeAsync()
        {
            await _kafka.DisposeAsync();
            await _rabbitMq.DisposeAsync();
            await _nats.DisposeAsync();
            await _localStack.DisposeAsync();
            await _http.DisposeAsync();
        }

        private async Task EnsureNatsStreamAsync()
        {
            await using var connection = new NatsConnection(new NatsOpts { Url = NatsUrl, Name = "nuntivus-it-nats" });
            await connection.ConnectAsync();
            var jetStream = new NatsJSContext(connection);

            try
            {
                await jetStream.GetStreamAsync("NUNTIVUS_DELIVERY_IT");
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                await jetStream.CreateStreamAsync(new StreamConfig("NUNTIVUS_DELIVERY_IT", new[] { "delivery.nats.>" })
                {
                    Storage = StreamConfigStorage.File,
                    Retention = StreamConfigRetention.Limits,
                    NumReplicas = 1,
                });
            }
        }

        private async Task EnsureSqsQueueAsync()
        {
            var create = await _localStack.ExecAsync(new[]
            {
                "awslocal",
                "sqs",
                "create-queue",
                "--queue-name",
                "nuntivus-delivery-it",
            });

            if (create.ExitCode != 0)
                throw new InvalidOperationException($"Failed to create LocalStack queue: {create.Stderr}");

            using var doc = JsonDocument.Parse(create.Stdout);
            SqsQueueUrl = doc.RootElement.GetProperty("QueueUrl").GetString()
                ?? throw new InvalidOperationException("LocalStack create-queue response did not include QueueUrl.");
        }
    }
}

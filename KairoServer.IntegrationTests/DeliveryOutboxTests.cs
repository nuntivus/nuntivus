using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Matching;
using KairoServer.Application.Messaging;
using KairoServer.Application.Persistence;
using KairoServer.Contracts.Deliveries;
using KairoServer.Contracts.Events;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Customers;
using KairoServer.Domain.Events;
using KairoServer.Domain.Outbox;
using KairoServer.Worker.Outbox;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(MatcherCollection))]
    public sealed class DeliveryOutboxTests : IAsyncLifetime
    {
        private readonly MatcherFixture _fixture;
        private readonly ServiceProvider _services;

        public DeliveryOutboxTests(MatcherFixture fixture)
        {
            _fixture = fixture;
            _services = fixture.BuildServiceProvider();
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public async Task DisposeAsync() => await _services.DisposeAsync();

        // ---- seed helpers -----------------------------------------------------

        private async Task SeedCustomerAsync(string id)
        {
            var now = DateTimeOffset.UtcNow;
            var customer = Customer.Create(id, id, now);
            await using var scope = _services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            uow.Begin(true);
            (await repo.InsertAsync(customer, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();
        }

        private async Task SeedContractAsync(string customerId, string[] eventTypes)
        {
            var contract = Contract.Create(
                customerId,
                eventTypes,
                schemaTargets: null,
                filter: null,
                new Destination(DestinationType.Http, "{\"Url\":\"https://x.example.com\"}"),
                DateTimeOffset.UtcNow);

            await using var scope = _services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IContractRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            uow.Begin(true);
            (await repo.InsertAsync(contract, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();
        }

        private async Task<Event> SeedEventAsync(string type, string payload = "{\"k\":\"v\"}")
        {
            var @event = Event.Create(
                idempotencyKey: Guid.NewGuid().ToString(),
                type: type,
                sourceId: Guid.NewGuid().ToString(),
                occurredAt: DateTimeOffset.UtcNow,
                payload: payload,
                metadata: (string?)null);

            await using var scope = _services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            uow.Begin(true);
            (await repo.InsertAsync(@event, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();
            return @event;
        }

        // ---- tests -----------------------------------------------------------

        [Fact]
        public async Task Matcher_emits_delivery_ready_outbox_row_per_delivery_in_same_transaction()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync("acme", new[] { "orders.created" });
            await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created", "{\"orderId\":\"o-1\"}");

            var matcher = _services.GetRequiredService<IContractMatcherService>();
            (await matcher.MatchAsync(
                new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt),
                CancellationToken.None)).EnsureSuccess();

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var outboxRows = (await conn.QueryAsync<(Guid id, Guid event_id, string type, string payload)>(
                "SELECT id, event_id, type, payload::text AS payload FROM outbox_messages WHERE type = @t AND event_id = @e",
                new { t = OutboxMessageType.DeliveryReady, e = @event.Id })).AsList();

            Assert.Equal(2, outboxRows.Count);
            foreach (var row in outboxRows)
            {
                var ready = JsonSerializer.Deserialize<DeliveryReady>(row.payload);
                Assert.NotNull(ready);
                Assert.Equal(@event.Id, ready!.EventId);
                Assert.Equal("acme", ready.CustomerId);
                Assert.Equal("orders.created", ready.EventType);
                Assert.Contains("o-1", ready.Payload);
            }
        }

        [Fact]
        public async Task Matcher_rollback_leaves_no_outbox_rows()
        {
            // Event referenced by the matcher does not exist in the DB → matcher returns
            // failure and rolls back the transaction (see ContractMatcherService).
            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var result = await matcher.MatchAsync(
                new EventAccepted(Guid.NewGuid(), "orders.created", "src", DateTimeOffset.UtcNow),
                CancellationToken.None);

            Assert.False(result.IsSuccess);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM outbox_messages WHERE type = @t",
                new { t = OutboxMessageType.DeliveryReady });
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task Outbox_worker_publishes_delivery_ready_to_jetstream_and_marks_processed()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created");

            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var matcher = _services.GetRequiredService<IContractMatcherService>();
            (await matcher.MatchAsync(
                new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt),
                CancellationToken.None)).EnsureSuccess();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
            var runTask = worker.StartAsync(cts.Token);

            long processed = 0;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
                processed = await conn.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM outbox_messages WHERE type = @t AND processed_at IS NOT NULL",
                    new { t = OutboxMessageType.DeliveryReady });
                if (processed > 0) break;
                await Task.Delay(200);
            }

            await worker.StopAsync(CancellationToken.None);
            await runTask;

            Assert.Equal(1, processed);

            // Verify the message reached JetStream on delivery.ready.
            var received = await FetchOneAsync(js, natsOption.Stream, "delivery.ready", "dr-verify");
            Assert.NotNull(received);
            var ready = JsonSerializer.Deserialize<DeliveryReady>(Encoding.UTF8.GetString(received!));
            Assert.NotNull(ready);
            Assert.Equal(@event.Id, ready!.EventId);
        }

        [Fact]
        public async Task Duplicate_publish_with_same_message_id_is_deduped_by_jetstream()
        {
            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var publisher = _services.GetRequiredService<IEventBusPublisher>();
            var msgId = Guid.NewGuid().ToString();
            var payload = JsonSerializer.Serialize(new DeliveryReady(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "acme", "orders.created", "{}", DateTimeOffset.UtcNow));

            (await publisher.PublishAsync("delivery.ready", payload, msgId, CancellationToken.None)).EnsureSuccess();
            (await publisher.PublishAsync("delivery.ready", payload, msgId, CancellationToken.None)).EnsureSuccess();

            var stream = await js.GetStreamAsync(natsOption.Stream);
            // Only one message stored despite two publishes with the same Nats-Msg-Id.
            Assert.Equal(1L, (long)stream.Info.State.Messages);
        }

        [Fact]
        public async Task Publisher_failure_increments_attempt_and_leaves_row_unprocessed()
        {
            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            // Insert an outbox row on a subject NOT captured by any stream — publish
            // will fail because JetStream rejects "no stream matched subject".
            var badMessage = OutboxMessage.Create(
                Guid.NewGuid(),
                type: "nomatch.subject",
                payload: "{\"x\":1}",
                createdAt: DateTimeOffset.UtcNow);

            await using (var scope = _services.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                uow.Begin(true);
                (await repo.InsertAsync(badMessage, uow, CancellationToken.None)).EnsureSuccess();
                uow.Commit();
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
            var runTask = worker.StartAsync(cts.Token);

            int attempts = 0;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
                attempts = await conn.ExecuteScalarAsync<int>(
                    "SELECT attempt_count FROM outbox_messages WHERE id = @id",
                    new { id = badMessage.Id });
                if (attempts >= 1) break;
                await Task.Delay(200);
            }

            await worker.StopAsync(CancellationToken.None);
            await runTask;

            Assert.True(attempts >= 1, $"Expected attempt_count>=1, got {attempts}.");

            await using var conn2 = new NpgsqlConnection(_fixture.ConnectionString);
            var processedAt = await conn2.ExecuteScalarAsync<DateTimeOffset?>(
                "SELECT processed_at FROM outbox_messages WHERE id = @id",
                new { id = badMessage.Id });
            Assert.Null(processedAt);
        }

        [Fact]
        public async Task Outbox_worker_does_not_collapse_distinct_messages_for_same_event()
        {
            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var @event = await SeedEventAsync("orders.created", "{\"orderId\":\"shared-event\"}");
            var subject = $"event.test.{Guid.NewGuid():N}";

            var m1 = OutboxMessage.Create(@event.Id, subject, "{\"n\":1}", DateTimeOffset.UtcNow);
            var m2 = OutboxMessage.Create(@event.Id, subject, "{\"n\":2}", DateTimeOffset.UtcNow);

            await using (var scope = _services.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                uow.Begin(true);
                (await repo.InsertAsync(m1, uow, CancellationToken.None)).EnsureSuccess();
                (await repo.InsertAsync(m2, uow, CancellationToken.None)).EnsureSuccess();
                uow.Commit();
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
            var runTask = worker.StartAsync(cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
                var processed = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM outbox_messages WHERE id = ANY(@ids) AND processed_at IS NOT NULL",
                    new { ids = new[] { m1.Id, m2.Id } });
                if (processed == 2)
                {
                    break;
                }

                await Task.Delay(200);
            }

            await worker.StopAsync(CancellationToken.None);
            await runTask;

            var consumed = await FetchCountAsync(js, natsOption.Stream, subject, $"verify-{Guid.NewGuid():N}", expected: 2);
            Assert.Equal(2, consumed);
        }

        // ---- helpers ---------------------------------------------------------

        private static async Task EnsureStreamAsync(INatsJSContext js, Infastructure.Messaging.Nats.NatsOption options)
        {
            var config = new StreamConfig(name: options.Stream, subjects: options.Subjects)
            {
                Retention = StreamConfigRetention.Limits,
                Storage = StreamConfigStorage.File,
                NumReplicas = 1,
                DuplicateWindow = options.DuplicateWindow,
            };
            try
            {
                await js.GetStreamAsync(options.Stream);
                await js.UpdateStreamAsync(config);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                await js.CreateStreamAsync(config);
            }
        }

        private static async Task<byte[]?> FetchOneAsync(
            INatsJSContext js, string stream, string filterSubject, string durable)
        {
            var consumer = await js.CreateOrUpdateConsumerAsync(
                stream,
                new ConsumerConfig(durable)
                {
                    FilterSubject = filterSubject,
                    AckPolicy = ConsumerConfigAckPolicy.Explicit,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                });

            var fetchOpts = new NatsJSFetchOpts { MaxMsgs = 1, Expires = TimeSpan.FromSeconds(5) };
            await foreach (var msg in consumer.FetchAsync<byte[]>(fetchOpts))
            {
                await msg.AckAsync();
                return msg.Data;
            }
            return null;
        }

        private static async Task<int> FetchCountAsync(
            INatsJSContext js,
            string stream,
            string filterSubject,
            string durable,
            int expected)
        {
            var consumer = await js.CreateOrUpdateConsumerAsync(
                stream,
                new ConsumerConfig(durable)
                {
                    FilterSubject = filterSubject,
                    AckPolicy = ConsumerConfigAckPolicy.Explicit,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                });

            var count = 0;
            var fetchOpts = new NatsJSFetchOpts { MaxMsgs = expected, Expires = TimeSpan.FromSeconds(5) };
            await foreach (var msg in consumer.FetchAsync<byte[]>(fetchOpts))
            {
                await msg.AckAsync();
                count++;
            }

            return count;
        }
    }
}

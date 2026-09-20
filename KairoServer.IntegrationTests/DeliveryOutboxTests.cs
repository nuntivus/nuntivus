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
using KairoServer.Domain.Deliveries;
using KairoServer.Domain.Events;
using KairoServer.Domain.Outbox;
using KairoServer.Infastructure.Messaging.Nats;
using KairoServer.Worker.Deliveries;
using KairoServer.Worker.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(MatcherCollection))]
    public sealed class DeliveryOutboxTests : IAsyncLifetime
    {
        private static readonly DeliveryFactory DeliveryFactory = new();

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

        private async Task<Guid> SeedContractAsync(string customerId, string[] eventTypes)
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

            return contract.Id;
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

        private async Task<Delivery> SeedScheduledDeliveryAsync(
            Event @event,
            Guid contractId,
            string customerId,
            DateTimeOffset scheduledAt)
        {
            var delivery = DeliveryFactory.CreatePending(
                @event.Id,
                contractId,
                customerId,
                @event.Type,
                @event.Payload,
                new Destination(DestinationType.Http, "{\"Url\":\"https://x.example.com\"}"),
                DateTimeOffset.UtcNow);

            await using var scope = _services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            uow.Begin(true);
            (await repo.InsertPendingAsync(new[] { delivery }, uow, CancellationToken.None)).EnsureSuccess();
            (await repo.ScheduleNextAttemptAsync(
                delivery.Id,
                delivery.UpdatedAt,
                scheduledAt,
                uow,
                CancellationToken.None)).EnsureSuccess();
            uow.Commit();

            return delivery;
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
        public async Task Two_outbox_workers_do_not_double_publish_same_row()
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
            var workerA = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
            var workerB = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
            var runA = workerA.StartAsync(cts.Token);
            var runB = workerB.StartAsync(cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
                var processed = await conn.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM outbox_messages WHERE type = @t AND event_id = @e AND processed_at IS NOT NULL",
                    new { t = OutboxMessageType.DeliveryReady, e = @event.Id });
                if (processed == 1)
                {
                    break;
                }

                await Task.Delay(200);
            }

            await Task.WhenAll(workerA.StopAsync(CancellationToken.None), workerB.StopAsync(CancellationToken.None));
            await Task.WhenAll(runA, runB);

            await using var verifyConn = new NpgsqlConnection(_fixture.ConnectionString);
            var processedRows = await verifyConn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM outbox_messages WHERE type = @t AND event_id = @e AND processed_at IS NOT NULL",
                new { t = OutboxMessageType.DeliveryReady, e = @event.Id });
            Assert.Equal(1, processedRows);
        }

        [Fact]
        public async Task Delivery_scheduler_worker_enqueues_due_delivery_once()
        {
            await SeedCustomerAsync("acme");
            var contractId = await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created");
            var delivery = await SeedScheduledDeliveryAsync(@event, contractId, "acme", DateTimeOffset.UtcNow.AddSeconds(-1));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var worker = ActivatorUtilities.CreateInstance<DeliverySchedulerWorker>(_services);
            var runTask = worker.StartAsync(cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            long count = 0;
            while (DateTime.UtcNow < deadline)
            {
                await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
                count = await conn.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM outbox_messages WHERE type = @t AND event_id = @e",
                    new { t = OutboxMessageType.DeliveryReady, e = @event.Id });
                if (count >= 1)
                    break;

                await Task.Delay(100);
            }

            await worker.StopAsync(CancellationToken.None);
            await runTask;

            Assert.Equal(1, count);

            await using var verifyConn = new NpgsqlConnection(_fixture.ConnectionString);
            var row = await verifyConn.QuerySingleAsync<(string status, DateTimeOffset? next_attempt_at)>(
                "SELECT status, next_attempt_at FROM deliveries WHERE id = @id",
                new { id = delivery.Id });

            Assert.Equal("Pending", row.status);
            Assert.Null(row.next_attempt_at);
        }

        [Fact]
        public async Task Two_delivery_scheduler_workers_do_not_duplicate_enqueues()
        {
            await SeedCustomerAsync("acme");
            var contractId = await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created");
            await SeedScheduledDeliveryAsync(@event, contractId, "acme", DateTimeOffset.UtcNow.AddSeconds(-1));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var workerA = ActivatorUtilities.CreateInstance<DeliverySchedulerWorker>(_services);
            var workerB = ActivatorUtilities.CreateInstance<DeliverySchedulerWorker>(_services);
            var runA = workerA.StartAsync(cts.Token);
            var runB = workerB.StartAsync(cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
                var count = await conn.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM outbox_messages WHERE type = @t AND event_id = @e",
                    new { t = OutboxMessageType.DeliveryReady, e = @event.Id });

                if (count == 1)
                    break;

                await Task.Delay(100);
            }

            await Task.WhenAll(workerA.StopAsync(CancellationToken.None), workerB.StopAsync(CancellationToken.None));
            await Task.WhenAll(runA, runB);

            await using var verifyConn = new NpgsqlConnection(_fixture.ConnectionString);
            var outboxRows = await verifyConn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM outbox_messages WHERE type = @t AND event_id = @e",
                new { t = OutboxMessageType.DeliveryReady, e = @event.Id });

            Assert.Equal(1, outboxRows);
        }

        [Fact]
        public async Task Duplicate_publish_with_same_message_id_is_deduped_by_jetstream()
        {
            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var publisher = _services.GetRequiredService<IEventBusPublisher>();
            var msgId = Guid.NewGuid().ToString();
            var subject = $"delivery.dedupe.{Guid.NewGuid():N}";
            var payload = JsonSerializer.Serialize(new DeliveryReady(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "acme", "orders.created", "{}", DateTimeOffset.UtcNow));

            (await publisher.PublishAsync(subject, payload, msgId, CancellationToken.None)).EnsureSuccess();
            await publisher.PublishAsync(subject, payload, msgId, CancellationToken.None);

            var received = await FetchCountAsync(js, natsOption.Stream, subject, $"dup-{Guid.NewGuid():N}", expected: 2);
            Assert.Equal(1, received);
        }

        [Fact]
        public async Task Outbox_worker_recovers_after_crash_before_publish()
        {
            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var subject = $"delivery.crash.before.{Guid.NewGuid():N}";
            var message = await SeedOutboxMessageAsync(subject);

            await using (var crashedServices = _fixture.BuildServiceProvider(configureServices: services =>
            {
                services.RemoveAll<IEventBusPublisher>();
                services.AddSingleton<IEventBusPublisher>(new AlwaysFailPublisher());
            }))
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(crashedServices);
                var runTask = worker.StartAsync(cts.Token);

                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < deadline)
                {
                    if (await ReadAttemptCountAsync(message.Id) > 0)
                        break;

                    await Task.Delay(150);
                }

                await worker.StopAsync(CancellationToken.None);
                await runTask;
            }

            Assert.Null(await ReadProcessedAtAsync(message.Id));
            await ForceOutboxRetryNowAsync(message.Id);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(35)))
            {
                var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
                var runTask = worker.StartAsync(cts.Token);

                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    if (await ReadProcessedAtAsync(message.Id) is not null)
                        break;

                    await Task.Delay(200);
                }

                await worker.StopAsync(CancellationToken.None);
                await runTask;
            }

            Assert.NotNull(await ReadProcessedAtAsync(message.Id));
            var consumed = await FetchCountAsync(js, natsOption.Stream, subject, $"before-{Guid.NewGuid():N}", expected: 2);
            Assert.Equal(1, consumed);
        }

        [Fact]
        public async Task Outbox_worker_recovers_after_crash_after_jetstream_ack()
        {
            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var subject = $"delivery.crash.afterack.{Guid.NewGuid():N}";
            var message = await SeedOutboxMessageAsync(subject);

            await using (var crashedServices = _fixture.BuildServiceProvider(configureServices: services =>
            {
                services.RemoveAll<IEventBusPublisher>();
                services.AddSingleton<IEventBusPublisher>(sp =>
                {
                    var inner = new NatsEventBusPublisher(
                        sp.GetRequiredService<INatsJSContext>(),
                        sp.GetRequiredService<NatsOption>(),
                        sp.GetRequiredService<KairoServer.Common.Logging.IAppLogger<NatsEventBusPublisher>>());
                    return new AckThenCrashPublisher(inner);
                });
            }))
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(crashedServices);
                var runTask = worker.StartAsync(cts.Token);

                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < deadline)
                {
                    if (await ReadAttemptCountAsync(message.Id) > 0)
                        break;

                    await Task.Delay(150);
                }

                await worker.StopAsync(CancellationToken.None);
                await runTask;
            }

            Assert.True(await ReadAttemptCountAsync(message.Id) > 0);
            Assert.Null(await ReadProcessedAtAsync(message.Id));
            await ForceOutboxRetryNowAsync(message.Id);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
                var runTask = worker.StartAsync(cts.Token);

                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    if (await ReadProcessedAtAsync(message.Id) is not null)
                        break;

                    await Task.Delay(200);
                }

                await worker.StopAsync(CancellationToken.None);
                await runTask;
            }

            Assert.True(await ReadAttemptCountAsync(message.Id) >= 1);
            var consumed = await FetchCountAsync(js, natsOption.Stream, subject, $"afterack-{Guid.NewGuid():N}", expected: 3);
            Assert.Equal(1, consumed);
        }

        [Fact]
        public async Task Outbox_worker_recovers_after_nats_outage()
        {
            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var subject = $"delivery.nats.outage.{Guid.NewGuid():N}";
            var message = await SeedOutboxMessageAsync(subject);

            await _fixture.StopNatsAsync();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
                var runTask = worker.StartAsync(cts.Token);

                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < deadline)
                {
                    if (await ReadAttemptCountAsync(message.Id) > 0)
                        break;

                    await Task.Delay(150);
                }

                await worker.StopAsync(CancellationToken.None);
                await runTask;
            }
            finally
            {
                await _fixture.StartNatsAsync();
            }

            Assert.Null(await ReadProcessedAtAsync(message.Id));
            await ForceOutboxRetryNowAsync(message.Id);

            await using (var recoveredServices = _fixture.BuildServiceProvider())
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(recoveredServices);
                var runTask = worker.StartAsync(cts.Token);

                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (DateTime.UtcNow < deadline)
                {
                    if (await ReadProcessedAtAsync(message.Id) is not null)
                        break;

                    await Task.Delay(250);
                }

                await worker.StopAsync(CancellationToken.None);
                await runTask;

                var recoveredJs = recoveredServices.GetRequiredService<INatsJSContext>();
                var recoveredOptions = recoveredServices.GetRequiredService<NatsOption>();
                var consumed = await FetchCountAsync(recoveredJs, recoveredOptions.Stream, subject, $"nats-outage-{Guid.NewGuid():N}", expected: 2);
                Assert.Equal(1, consumed);
            }

            Assert.NotNull(await ReadProcessedAtAsync(message.Id));
        }

        [Fact]
        public async Task Publisher_failure_increments_attempt_and_leaves_row_unprocessed()
        {
            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            // Insert an outbox row on a subject NOT captured by any stream — publish
            // will fail because JetStream rejects "no stream matched subject".
            var @event = await SeedEventAsync("orders.created");
            var badMessage = OutboxMessage.Create(
                @event.Id,
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

            var failureState = await conn2.QuerySingleAsync<(DateTimeOffset? last_failed_at, DateTimeOffset? next_attempt_at, string? last_error)>(
                "SELECT last_failed_at, next_attempt_at, last_error FROM outbox_messages WHERE id = @id",
                new { id = badMessage.Id });

            Assert.NotNull(failureState.last_failed_at);
            Assert.NotNull(failureState.next_attempt_at);
            Assert.True(failureState.next_attempt_at > failureState.last_failed_at);
            Assert.False(string.IsNullOrWhiteSpace(failureState.last_error));
        }

        [Fact]
        public async Task Outbox_worker_skips_rows_scheduled_for_future_retry()
        {
            var @event = await SeedEventAsync("orders.created");
            var blocked = OutboxMessage.Create(
                @event.Id,
                type: "nomatch.subject",
                payload: "{\"x\":2}",
                createdAt: DateTimeOffset.UtcNow);

            await using (var scope = _services.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                uow.Begin(true);
                (await repo.InsertAsync(blocked, uow, CancellationToken.None)).EnsureSuccess();
                uow.Commit();
            }

            var lockoutUntil = DateTimeOffset.UtcNow.AddMinutes(10);
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.ExecuteAsync(
                    "UPDATE outbox_messages SET next_attempt_at = @next WHERE id = @id",
                    new { id = blocked.Id, next = lockoutUntil });
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var worker = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
            var runTask = worker.StartAsync(cts.Token);
            await Task.Delay(1200);
            await worker.StopAsync(CancellationToken.None);
            await runTask;

            await using var verify = new NpgsqlConnection(_fixture.ConnectionString);
            var state = await verify.QuerySingleAsync<(int attempts, DateTimeOffset? processed_at)>(
                "SELECT attempt_count AS attempts, processed_at FROM outbox_messages WHERE id = @id",
                new { id = blocked.Id });

            Assert.Equal(0, state.attempts);
            Assert.Null(state.processed_at);
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

        [Fact]
        public async Task Outbox_load_processes_messages_with_target_throughput()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("RUN_OUTBOX_LOAD_TESTS"), "1", StringComparison.Ordinal))
            {
                return;
            }

            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var messageCount = int.TryParse(Environment.GetEnvironmentVariable("OUTBOX_LOAD_MESSAGE_COUNT"), out var parsedCount)
                ? Math.Max(50, parsedCount)
                : 500;

            var minThroughputPerSecond = double.TryParse(Environment.GetEnvironmentVariable("OUTBOX_LOAD_MIN_THROUGHPUT"), out var parsedThroughput)
                ? Math.Max(1, parsedThroughput)
                : 25d;

            var deadlineSeconds = int.TryParse(Environment.GetEnvironmentVariable("OUTBOX_LOAD_DEADLINE_SECONDS"), out var parsedDeadline)
                ? Math.Max(5, parsedDeadline)
                : 30;

            var @event = await SeedEventAsync("orders.created", "{\"load\":true}");
            var subject = $"delivery.load.{Guid.NewGuid():N}";

            await using (var scope = _services.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                uow.Begin(true);

                for (var i = 0; i < messageCount; i++)
                {
                    var payload = JsonSerializer.Serialize(new { n = i, e = @event.Id });
                    var message = OutboxMessage.Create(@event.Id, subject, payload, DateTimeOffset.UtcNow);
                    (await repo.InsertAsync(message, uow, CancellationToken.None)).EnsureSuccess();
                }

                uow.Commit();
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(deadlineSeconds + 5));
            var workerA = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);
            var workerB = ActivatorUtilities.CreateInstance<OutboxPublisherWorker>(_services);

            var startedAt = DateTimeOffset.UtcNow;
            var runA = workerA.StartAsync(cts.Token);
            var runB = workerB.StartAsync(cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(deadlineSeconds);
            var processed = 0;
            while (DateTime.UtcNow < deadline)
            {
                await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
                processed = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM outbox_messages WHERE type = @type AND processed_at IS NOT NULL",
                    new { type = subject });

                if (processed >= messageCount)
                {
                    break;
                }

                await Task.Delay(250);
            }

            var elapsed = DateTimeOffset.UtcNow - startedAt;

            await Task.WhenAll(workerA.StopAsync(CancellationToken.None), workerB.StopAsync(CancellationToken.None));
            await Task.WhenAll(runA, runB);

            Assert.True(processed >= messageCount, $"Load target not reached in time. Processed={processed}, expected={messageCount}, deadline={deadlineSeconds}s.");

            var throughput = processed / Math.Max(1d, elapsed.TotalSeconds);
            Assert.True(
                throughput >= minThroughputPerSecond,
                $"Outbox throughput {throughput:F2}/s was below target {minThroughputPerSecond:F2}/s for {processed} messages in {elapsed.TotalSeconds:F2}s.");
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

        private async Task<OutboxMessage> SeedOutboxMessageAsync(string subject)
        {
            var @event = await SeedEventAsync("orders.created", "{\"orderId\":\"o-chaos\"}");
            var payload = JsonSerializer.Serialize(new DeliveryReady(
                Guid.NewGuid(),
                @event.Id,
                Guid.NewGuid(),
                "acme",
                "orders.created",
                "{\"orderId\":\"o-chaos\"}",
                DateTimeOffset.UtcNow));

            var message = OutboxMessage.Create(@event.Id, subject, payload, DateTimeOffset.UtcNow);

            await using var scope = _services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            uow.Begin(true);
            (await repo.InsertAsync(message, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();

            return message;
        }

        private async Task<int> ReadAttemptCountAsync(Guid outboxId)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            return await conn.ExecuteScalarAsync<int>(
                "SELECT attempt_count FROM outbox_messages WHERE id = @id",
                new { id = outboxId });
        }

        private async Task<DateTime?> ReadProcessedAtAsync(Guid outboxId)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            return await conn.ExecuteScalarAsync<DateTime?>(
                "SELECT processed_at FROM outbox_messages WHERE id = @id",
                new { id = outboxId });
        }

        private async Task ForceOutboxRetryNowAsync(Guid outboxId)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.ExecuteAsync(
                "UPDATE outbox_messages SET next_attempt_at = NOW(), claimed_by = NULL, claim_expires_at = NULL WHERE id = @id",
                new { id = outboxId });
        }

        private sealed class AlwaysFailPublisher : IEventBusPublisher
        {
            public Task<KairoServer.Common.Result> PublishAsync(string subject, string payload, string messageId, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Simulated crash before publish.");
            }
        }

        private sealed class AckThenCrashPublisher : IEventBusPublisher
        {
            private readonly IEventBusPublisher _inner;

            public AckThenCrashPublisher(IEventBusPublisher inner)
            {
                _inner = inner;
            }

            public async Task<KairoServer.Common.Result> PublishAsync(string subject, string payload, string messageId, CancellationToken cancellationToken)
            {
                var result = await _inner.PublishAsync(subject, payload, messageId, cancellationToken);
                result.EnsureSuccess();
                throw new InvalidOperationException("Simulated crash after JetStream ACK and before outbox mark-processed.");
            }
        }
    }
}

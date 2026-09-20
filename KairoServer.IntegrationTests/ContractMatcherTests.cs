using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Events;
using KairoServer.Application.Matching;
using KairoServer.Application.Messaging;
using KairoServer.Application.Persistence;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Events;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Customers;
using KairoServer.Domain.Events;
using KairoServer.Domain.Outbox;
using KairoServer.Worker.Matching;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(MatcherCollection))]
    public sealed class ContractMatcherTests : IAsyncLifetime
    {
        private readonly MatcherFixture _fixture;
        private readonly ServiceProvider _services;

        public ContractMatcherTests(MatcherFixture fixture)
        {
            _fixture = fixture;
            _services = fixture.BuildServiceProvider();
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public async Task DisposeAsync() => await _services.DisposeAsync();

        // ---- seed helpers -----------------------------------------------------

        private async Task<string> SeedCustomerAsync(string id, bool enabled = true)
        {
            var now = DateTimeOffset.UtcNow;
            var customer = Customer.Create(id, id, now);
            if (!enabled) customer.Disable(now);

            await using var scope = _services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            uow.Begin(true);
            (await repo.InsertAsync(customer, uow, CancellationToken.None)).EnsureSuccess();
            if (!enabled)
                (await repo.UpdateAsync(customer, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();
            return id;
        }

        private async Task<Guid> SeedContractAsync(
            string customerId,
            string[] eventTypes,
            bool enabled = true,
            ContractFilter? filter = null,
            Destination? destination = null,
            IReadOnlyCollection<ContractSchemaTarget>? schemaTargets = null,
            ContractPayloadTransformation? transformation = null)
        {
            var now = DateTimeOffset.UtcNow;
            var contract = Contract.Create(
                customerId,
                eventTypes,
                schemaTargets,
                filter,
                destination ?? new Destination(DestinationType.Http, "{\"Url\":\"https://x.example.com\"}"),
                now,
                transformation: transformation);
            if (!enabled) contract.Disable(now);

            await using var scope = _services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IContractRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            uow.Begin(true);
            (await repo.InsertAsync(contract, uow, CancellationToken.None)).EnsureSuccess();
            if (!enabled)
                (await repo.UpdateAsync(contract, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();
            return contract.Id;
        }

        private async Task<Event> SeedEventAsync(
            string type,
            string payload = "{\"k\":\"v\"}",
            string? metadata = null)
        {
            var @event = Event.Create(
                idempotencyKey: Guid.NewGuid().ToString(),
                type: type,
                sourceId: Guid.NewGuid().ToString(),
                occurredAt: DateTimeOffset.UtcNow,
                payload: payload,
                metadata: metadata);

            await using var scope = _services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            uow.Begin(true);
            (await repo.InsertAsync(@event, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();
            return @event;
        }

        private async Task<long> CountDeliveriesAsync(Guid eventId)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            return await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM deliveries WHERE event_id = @id", new { id = eventId });
        }

        private async Task<long> CountDeliveryReadyOutboxAsync(Guid eventId)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            return await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM outbox_messages WHERE event_id = @id AND type = @type",
                new { id = eventId, type = OutboxMessageType.DeliveryReady });
        }

        // ---- service-level tests ---------------------------------------------

        [Fact]
        public async Task Matches_enabled_contract_and_creates_pending_delivery()
        {
            await SeedCustomerAsync("acme");
            var contractId = await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created", "{\"orderId\":\"o-1\"}");

            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var result = await matcher.MatchAsync(
                new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt),
                CancellationToken.None);

            result.EnsureSuccess();
            Assert.Equal(1, result.Value.MatchedContracts);
            Assert.Equal(1, result.Value.InsertedDeliveries);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var row = await conn.QuerySingleAsync<(Guid contract_id, string customer_id, string event_type, string status, string payload)>(
                "SELECT contract_id, customer_id, event_type, status, payload::text AS payload FROM deliveries WHERE event_id = @id",
                new { id = @event.Id });

            Assert.Equal(contractId, row.contract_id);
            Assert.Equal("acme", row.customer_id);
            Assert.Equal("orders.created", row.event_type);
            Assert.Equal("Pending", row.status);
            Assert.Contains("o-1", row.payload);
        }

        [Fact]
        public async Task Snapshot_fields_are_persisted_per_delivery_and_remain_stable_after_contract_edit()
        {
            await SeedCustomerAsync("acme");

            var initialDestination = new Destination(
                DestinationType.Http,
                "{\"Url\":\"https://first.example.com\",\"Authentication\":{\"Type\":\"hmac\",\"Credential\":{\"Kind\":\"ref\",\"Value\":\"secret.acme.v1\",\"Version\":1}}}");

            var contractId = await SeedContractAsync(
                "acme",
                new[] { "orders.created" },
                destination: initialDestination,
                schemaTargets: new[] { new ContractSchemaTarget("orders.created", 1) },
                transformation: new ContractPayloadTransformation(
                    1,
                    new[] { new ContractPayloadFieldMapping("order.id", "orderId") }));

            var firstEvent = await SeedEventAsync("orders.created", "{\"order\":{\"id\":\"first\"}}");
            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var firstMatch = await matcher.MatchAsync(
                new EventAccepted(firstEvent.Id, firstEvent.Type, firstEvent.SourceId, firstEvent.OccurredAt),
                CancellationToken.None);
            firstMatch.EnsureSuccess();
            Assert.Equal(1, firstMatch.Value.InsertedDeliveries);

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync(
                    """
                    UPDATE contracts
                    SET destination_config = @DestinationConfig::jsonb,
                        auth_ref = @AuthRef,
                        auth_version = @AuthVersion,
                        transformation_config = @TransformationConfig::jsonb,
                        updated_at = @UpdatedAt
                    WHERE id = @ContractId;
                    """,
                    new
                    {
                        ContractId = contractId,
                        DestinationConfig = "{\"Url\":\"https://second.example.com\",\"Authentication\":{\"Type\":\"hmac\",\"Credential\":{\"Kind\":\"ref\",\"Value\":\"secret.acme.v2\",\"Version\":2}}}",
                        AuthRef = "secret.acme.v2",
                        AuthVersion = 2,
                        TransformationConfig = "{\"Version\":2,\"FieldMappings\":[{\"SourceSelector\":\"order.id\",\"TargetSelector\":\"id\"}]}",
                        UpdatedAt = DateTimeOffset.UtcNow,
                    });

                await conn.ExecuteAsync(
                    "UPDATE contract_schema_targets SET schema_version = 2 WHERE contract_id = @ContractId AND event_type = @EventType;",
                    new { ContractId = contractId, EventType = "orders.created" });
            }

            await using var verifyConn = new NpgsqlConnection(_fixture.ConnectionString);
            await verifyConn.OpenAsync();

            var currentContractConfig = await verifyConn.ExecuteScalarAsync<string>(
                "SELECT destination_config::text FROM contracts WHERE id = @ContractId;",
                new { ContractId = contractId });

            var firstSnapshot = await verifyConn.QuerySingleAsync<(string destination_config, string? auth_ref, int? auth_version, int? transformation_version, int? schema_version)>(
                "SELECT destination_config::text, auth_ref, auth_version, transformation_version, schema_version FROM deliveries WHERE event_id = @EventId;",
                new { EventId = firstEvent.Id });

            Assert.Contains("second.example.com", currentContractConfig);

            Assert.Contains("first.example.com", firstSnapshot.destination_config);
            Assert.Equal("secret.acme.v1", firstSnapshot.auth_ref);
            Assert.Equal(1, firstSnapshot.auth_version);
            Assert.Equal(1, firstSnapshot.transformation_version);
            Assert.Equal(1, firstSnapshot.schema_version);
        }

        [Fact]
        public async Task Skips_disabled_contract()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync("acme", new[] { "orders.created" }, enabled: false);
            var @event = await SeedEventAsync("orders.created");

            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var result = await matcher.MatchAsync(
                new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt),
                CancellationToken.None);

            result.EnsureSuccess();
            Assert.Equal(0, result.Value.MatchedContracts);
            Assert.Equal(0, await CountDeliveriesAsync(@event.Id));
        }

        [Fact]
        public async Task Skips_contract_when_customer_disabled()
        {
            await SeedCustomerAsync("acme", enabled: false);
            await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created");

            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var result = await matcher.MatchAsync(
                new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt),
                CancellationToken.None);

            result.EnsureSuccess();
            Assert.Equal(0, result.Value.MatchedContracts);
            Assert.Equal(0, await CountDeliveriesAsync(@event.Id));
        }

        [Fact]
        public async Task Skips_contract_when_event_type_does_not_match()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.deleted");

            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var result = await matcher.MatchAsync(
                new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt),
                CancellationToken.None);

            result.EnsureSuccess();
            Assert.Equal(0, result.Value.InsertedDeliveries);
        }

        [Fact]
        public async Task Duplicate_processing_does_not_create_duplicate_deliveries()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created");

            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var accepted = new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt);

            var first = await matcher.MatchAsync(accepted, CancellationToken.None);
            var second = await matcher.MatchAsync(accepted, CancellationToken.None);

            first.EnsureSuccess();
            second.EnsureSuccess();
            Assert.Equal(1, first.Value.InsertedDeliveries);
            Assert.Equal(0, second.Value.InsertedDeliveries);
            Assert.Equal(1, await CountDeliveriesAsync(@event.Id));
            Assert.Equal(1, await CountDeliveryReadyOutboxAsync(@event.Id));
        }

        [Fact]
        public async Task Fans_out_one_delivery_per_matching_contract()
        {
            await SeedCustomerAsync("acme");
            await SeedCustomerAsync("globex");
            await SeedContractAsync("acme", new[] { "orders.created" });
            await SeedContractAsync("globex", new[] { "orders.created", "orders.updated" });
            var @event = await SeedEventAsync("orders.created");

            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var result = await matcher.MatchAsync(
                new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt),
                CancellationToken.None);

            result.EnsureSuccess();
            Assert.Equal(2, result.Value.InsertedDeliveries);
            Assert.Equal(2, await CountDeliveriesAsync(@event.Id));
        }

        [Fact]
        public async Task Applies_metadata_and_payload_filters_after_event_type_match()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync(
                "acme",
                new[] { "orders.created" },
                filter: new ContractFilter(
                    metadataRules: new[]
                    {
                        new ContractFilterRule("tenant", ContractFilterOperator.Equals, "acme", null)
                    },
                    payloadRules: new[]
                    {
                        new ContractFilterRule("order.amount", ContractFilterOperator.GreaterThanOrEqual, "100", null)
                    }));

            var @event = await SeedEventAsync(
                "orders.created",
                payload: "{\"order\":{\"amount\":150}}",
                metadata: "{\"tenant\":\"acme\"}");

            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var result = await matcher.MatchAsync(
                new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt),
                CancellationToken.None);

            result.EnsureSuccess();
            Assert.Equal(1, result.Value.MatchedContracts);
            Assert.Equal(1, await CountDeliveriesAsync(@event.Id));
        }

        [Fact]
        public async Task Missing_payload_path_or_type_mismatch_results_in_non_match()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync(
                "acme",
                new[] { "orders.created" },
                filter: new ContractFilter(
                    metadataRules: Array.Empty<ContractFilterRule>(),
                    payloadRules: new[]
                    {
                        new ContractFilterRule("order.amount", ContractFilterOperator.GreaterThan, "100", null)
                    }));

            var missingPathEvent = await SeedEventAsync(
                "orders.created",
                payload: "{\"order\":{\"total\":150}}",
                metadata: null);

            var typeMismatchEvent = await SeedEventAsync(
                "orders.created",
                payload: "{\"order\":{\"amount\":\"high\"}}",
                metadata: null);

            var matcher = _services.GetRequiredService<IContractMatcherService>();

            var first = await matcher.MatchAsync(
                new EventAccepted(missingPathEvent.Id, missingPathEvent.Type, missingPathEvent.SourceId, missingPathEvent.OccurredAt),
                CancellationToken.None);

            var second = await matcher.MatchAsync(
                new EventAccepted(typeMismatchEvent.Id, typeMismatchEvent.Type, typeMismatchEvent.SourceId, typeMismatchEvent.OccurredAt),
                CancellationToken.None);

            first.EnsureSuccess();
            second.EnsureSuccess();
            Assert.Equal(0, first.Value.MatchedContracts);
            Assert.Equal(0, second.Value.MatchedContracts);
            Assert.Equal(0, await CountDeliveriesAsync(missingPathEvent.Id));
            Assert.Equal(0, await CountDeliveriesAsync(typeMismatchEvent.Id));
        }

        [Fact]
        public async Task Replay_uses_current_contract_configuration_when_matching()
        {
            await SeedCustomerAsync("acme");
            var contractId = await SeedContractAsync("acme", new[] { "orders.created" });
            var sourceEvent = await SeedEventAsync("orders.created", "{\"orderId\":\"replay-me\"}");

            await using (var scope = _services.CreateAsyncScope())
            {
                var contracts = scope.ServiceProvider.GetRequiredService<IContractRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var contractResult = await contracts.GetByIdAsync(contractId, CancellationToken.None);
                var contract = contractResult.EnsureValue()!;

                contract.UpdateDestination(
                    new Destination(DestinationType.Http, "{\"Url\":\"https://replay.example.com\"}"),
                    DateTimeOffset.UtcNow);

                uow.Begin(true);
                (await contracts.UpdateAsync(contract, uow, CancellationToken.None)).EnsureSuccess();
                uow.Commit();
            }

            ReplayEventResult replay;
            await using (var scope = _services.CreateAsyncScope())
            {
                var eventService = new EventService(
                    scope.ServiceProvider.GetRequiredService<IEventRepository>(),
                    scope.ServiceProvider.GetRequiredService<IOutboxRepository>(),
                    scope.ServiceProvider.GetRequiredService<IAuditLogRepository>(),
                    scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                    scope.ServiceProvider.GetRequiredService<IAppLogger<EventService>>());

                var replayResult = await eventService.ReplayAsync(
                    sourceEvent.Id,
                    "ops-replay-current-config",
                    null,
                    CancellationToken.None);

                replay = replayResult.EnsureValue();
                Assert.True(replay.Created);
            }

            var replayedEvent = replay.ReplayedEventId;
            var matcher = _services.GetRequiredService<IContractMatcherService>();
            var match = await matcher.MatchAsync(
                new EventAccepted(replayedEvent, sourceEvent.Type, sourceEvent.SourceId, sourceEvent.OccurredAt),
                CancellationToken.None);

            match.EnsureSuccess();
            Assert.Equal(1, match.Value.InsertedDeliveries);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var destinationConfig = await conn.ExecuteScalarAsync<string>(
                "SELECT destination_config::text FROM deliveries WHERE event_id = @eventId LIMIT 1;",
                new { eventId = replayedEvent });

            Assert.Contains("https://replay.example.com", destinationConfig);
        }

        // ---- end-to-end pipeline via JetStream + Worker -----------------------

        [Fact]
        public async Task End_to_end_worker_consumes_event_accepted_and_creates_delivery()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created");

            // Bootstrap stream + publish event.accepted just like the outbox does.
            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var publisher = _services.GetRequiredService<IEventBusPublisher>();
            var payload = JsonSerializer.Serialize(new EventAccepted(
                @event.Id, @event.Type, @event.SourceId, @event.OccurredAt));

            (await publisher.PublishAsync("event.accepted", payload, @event.Id.ToString(), CancellationToken.None))
                .EnsureSuccess();

            // Run the matcher worker briefly and assert the delivery row was created.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var worker = ActivatorUtilities.CreateInstance<ContractMatcherWorker>(_services);
            var runTask = worker.StartAsync(cts.Token);

            long count = 0;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                count = await CountDeliveriesAsync(@event.Id);
                if (count > 0) break;
                await Task.Delay(200);
            }

            await worker.StopAsync(CancellationToken.None);
            await runTask;

            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Matcher_worker_restart_recovers_pending_message_without_duplicate_delivery()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created", "{\"restart\":true}");

            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var publisher = _services.GetRequiredService<IEventBusPublisher>();
            var payload = JsonSerializer.Serialize(new EventAccepted(
                @event.Id, @event.Type, @event.SourceId, @event.OccurredAt));
            (await publisher.PublishAsync("event.accepted", payload, $"restart-{Guid.NewGuid():N}", CancellationToken.None)).EnsureSuccess();

            using (var firstCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
            {
                var firstRun = ActivatorUtilities.CreateInstance<ContractMatcherWorker>(_services);
                var t1 = firstRun.StartAsync(firstCts.Token);
                await Task.Delay(300);
                await firstRun.StopAsync(CancellationToken.None);
                await t1;
            }

            using (var secondCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var secondRun = ActivatorUtilities.CreateInstance<ContractMatcherWorker>(_services);
                var t2 = secondRun.StartAsync(secondCts.Token);

                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    if (await CountDeliveriesAsync(@event.Id) >= 1)
                        break;

                    await Task.Delay(200);
                }

                await secondRun.StopAsync(CancellationToken.None);
                await t2;
            }

            Assert.Equal(1, await CountDeliveriesAsync(@event.Id));
            Assert.Equal(1, await CountDeliveryReadyOutboxAsync(@event.Id));
        }

        [Fact]
        public async Task Duplicate_jetstream_messages_are_safe_for_matcher()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync("acme", new[] { "orders.created" });
            var @event = await SeedEventAsync("orders.created", "{\"duplicate\":true}");

            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var publisher = _services.GetRequiredService<IEventBusPublisher>();
            var payload = JsonSerializer.Serialize(new EventAccepted(
                @event.Id, @event.Type, @event.SourceId, @event.OccurredAt));

            (await publisher.PublishAsync("event.accepted", payload, $"dup-a-{Guid.NewGuid():N}", CancellationToken.None)).EnsureSuccess();
            (await publisher.PublishAsync("event.accepted", payload, $"dup-b-{Guid.NewGuid():N}", CancellationToken.None)).EnsureSuccess();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = ActivatorUtilities.CreateInstance<ContractMatcherWorker>(_services);
            var runTask = worker.StartAsync(cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (await CountDeliveriesAsync(@event.Id) >= 1)
                    break;

                await Task.Delay(200);
            }

            await worker.StopAsync(CancellationToken.None);
            await runTask;

            Assert.Equal(1, await CountDeliveriesAsync(@event.Id));
            Assert.Equal(1, await CountDeliveryReadyOutboxAsync(@event.Id));
        }

        [Fact]
        public async Task Two_workers_process_disjoint_messages_via_shared_durable()
        {
            await SeedCustomerAsync("acme");
            await SeedContractAsync("acme", new[] { "orders.created" });

            var events = new List<Event>();
            for (var i = 0; i < 8; i++)
                events.Add(await SeedEventAsync("orders.created", $"{{\"i\":{i}}}"));

            var js = _services.GetRequiredService<INatsJSContext>();
            var natsOption = _services.GetRequiredService<Infastructure.Messaging.Nats.NatsOption>();
            await EnsureStreamAsync(js, natsOption);

            var publisher = _services.GetRequiredService<IEventBusPublisher>();
            foreach (var e in events)
            {
                var payload = JsonSerializer.Serialize(new EventAccepted(
                    e.Id, e.Type, e.SourceId, e.OccurredAt));
                (await publisher.PublishAsync("event.accepted", payload, e.Id.ToString(), CancellationToken.None))
                    .EnsureSuccess();
            }

            // Two workers share the same durable — JetStream distributes messages between them.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var worker1 = ActivatorUtilities.CreateInstance<ContractMatcherWorker>(_services);
            var worker2 = ActivatorUtilities.CreateInstance<ContractMatcherWorker>(_services);
            var t1 = worker1.StartAsync(cts.Token);
            var t2 = worker2.StartAsync(cts.Token);

            long total = 0;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
                total = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM deliveries;");
                if (total >= events.Count) break;
                await Task.Delay(250);
            }

            await Task.WhenAll(worker1.StopAsync(CancellationToken.None), worker2.StopAsync(CancellationToken.None));
            await Task.WhenAll(t1, t2);

            Assert.Equal(events.Count, total);
        }

        private static async Task EnsureStreamAsync(INatsJSContext js, Infastructure.Messaging.Nats.NatsOption options)
        {
            var config = new StreamConfig(name: options.Stream, subjects: options.Subjects)
            {
                Retention = StreamConfigRetention.Limits,
                Storage = StreamConfigStorage.File,
                NumReplicas = 1,
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
    }
}

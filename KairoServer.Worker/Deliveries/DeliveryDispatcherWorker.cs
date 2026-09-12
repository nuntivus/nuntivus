using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Deliveries;
using KairoServer.Infastructure.Messaging.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace KairoServer.Worker.Deliveries
{
    /// <summary>
    /// Consumes <c>delivery.ready</c> messages from JetStream via a durable pull
    /// consumer and dispatches each one through
    /// <see cref="IDeliveryDispatcherService"/>. Replicas share the durable so
    /// JetStream balances the work between them with server-side
    /// <c>MaxAckPending</c> flow control.
    /// </summary>
    public sealed class DeliveryDispatcherWorker : BackgroundService
    {
        private readonly INatsJSContext _jetStream;
        private readonly NatsOption _natsOptions;
        private readonly DeliveryOption _deliveryOptions;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAppLogger<DeliveryDispatcherWorker> _logger;

        public DeliveryDispatcherWorker(
            INatsJSContext jetStream,
            NatsOption natsOptions,
            DeliveryOption deliveryOptions,
            IServiceScopeFactory scopeFactory,
            IAppLogger<DeliveryDispatcherWorker> logger)
        {
            _jetStream = jetStream ?? throw new ArgumentNullException(nameof(jetStream));
            _natsOptions = natsOptions ?? throw new ArgumentNullException(nameof(natsOptions));
            _deliveryOptions = deliveryOptions ?? throw new ArgumentNullException(nameof(deliveryOptions));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information(
                "DeliveryDispatcherWorker starting (stream {Stream}, durable {Durable}, filter {Filter}).",
                _natsOptions.Stream, _deliveryOptions.DurableName, _deliveryOptions.FilterSubject);

            var consumer = await CreateConsumerAsync(stoppingToken);

            var fetchOpts = new NatsJSFetchOpts
            {
                MaxMsgs = _deliveryOptions.BatchSize,
                Expires = _deliveryOptions.FetchTimeout,
            };

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await foreach (var msg in consumer.FetchAsync<byte[]>(fetchOpts, cancellationToken: stoppingToken))
                    {
                        await ProcessMessageAsync(msg, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unexpected error in DeliveryDispatcherWorker fetch loop.");
                    try { await Task.Delay(_deliveryOptions.NakDelay, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }

            _logger.Information("DeliveryDispatcherWorker stopped.");
        }

        private async Task<INatsJSConsumer> CreateConsumerAsync(CancellationToken cancellationToken)
        {
            var config = new ConsumerConfig(_deliveryOptions.DurableName)
            {
                FilterSubject = _deliveryOptions.FilterSubject,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                MaxAckPending = _deliveryOptions.MaxAckPending,
                AckWait = _deliveryOptions.AckWait,
            };

            var consumer = await _jetStream.CreateOrUpdateConsumerAsync(
                _natsOptions.Stream, config, cancellationToken);

            _logger.Information(
                "JetStream durable consumer {Durable} ready on stream {Stream} (filter {Filter}).",
                _deliveryOptions.DurableName, _natsOptions.Stream, _deliveryOptions.FilterSubject);

            return consumer;
        }

        private async Task ProcessMessageAsync(NatsJSMsg<byte[]> msg, CancellationToken cancellationToken)
        {
            DeliveryReady? ready;
            try
            {
                var json = msg.Data is { Length: > 0 } bytes ? Encoding.UTF8.GetString(bytes) : null;
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.Warning("Received empty delivery.ready message; terminating.");
                    await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                    return;
                }
                ready = JsonSerializer.Deserialize<DeliveryReady>(json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deserialize delivery.ready payload; terminating message.");
                await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                return;
            }

            if (ready is null || ready.DeliveryId == Guid.Empty)
            {
                _logger.Warning("delivery.ready payload invalid; terminating message.");
                await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDeliveryDispatcherService>();

                var result = await dispatcher.DispatchAsync(ready.DeliveryId, cancellationToken);

                if (result.IsSuccess)
                {
                    await msg.AckAsync(cancellationToken: cancellationToken);
                }
                else
                {
                    _logger.Warning(
                        "Dispatcher failed for delivery {DeliveryId}: {Error}. NAK for redelivery.",
                        ready.DeliveryId, result.Error);
                    await msg.NakAsync(delay: _deliveryOptions.NakDelay, cancellationToken: cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try { await msg.NakAsync(delay: _deliveryOptions.NakDelay, cancellationToken: CancellationToken.None); }
                catch { /* best-effort */ }
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Unexpected error dispatching delivery {DeliveryId}. NAK for redelivery.",
                    ready.DeliveryId);
                await msg.NakAsync(delay: _deliveryOptions.NakDelay, cancellationToken: cancellationToken);
            }
        }
    }
}

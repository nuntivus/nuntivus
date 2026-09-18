using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Observability;
using KairoServer.Application.Persistence;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Deliveries;
using KairoServer.Domain.Deliveries;
using KairoServer.Infastructure.Messaging.Nats;
using KairoServer.Worker.Observability;
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
        private readonly NatsOption _natsOptions;
        private readonly DeliveryOption _deliveryOptions;
        private readonly IDurableConsumerFactory _consumerFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAppLogger<DeliveryDispatcherWorker> _logger;

        public DeliveryDispatcherWorker(
            NatsOption natsOptions,
            DeliveryOption deliveryOptions,
            IDurableConsumerFactory consumerFactory,
            IServiceScopeFactory scopeFactory,
            IAppLogger<DeliveryDispatcherWorker> logger)
        {
            _natsOptions = natsOptions ?? throw new ArgumentNullException(nameof(natsOptions));
            _deliveryOptions = deliveryOptions ?? throw new ArgumentNullException(nameof(deliveryOptions));
            _consumerFactory = consumerFactory ?? throw new ArgumentNullException(nameof(consumerFactory));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information(
                "DeliveryDispatcherWorker starting (stream {Stream}, durable {Durable}, filter {Filter}, parallelism {Parallelism}).",
                _natsOptions.Stream, _deliveryOptions.DurableName, _deliveryOptions.FilterSubject, _deliveryOptions.MaxConcurrentMessages);

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
                    using var throttle = new SemaphoreSlim(Math.Max(1, _deliveryOptions.MaxConcurrentMessages));
                    var processTasks = new List<Task>();

                    await foreach (var msg in consumer.FetchAsync<byte[]>(fetchOpts, cancellationToken: stoppingToken))
                    {
                        await throttle.WaitAsync(stoppingToken);
                        processTasks.Add(ProcessWithThrottleAsync(msg, throttle, stoppingToken));
                    }

                    if (processTasks.Count > 0)
                    {
                        await Task.WhenAll(processTasks);
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

        private async Task ProcessWithThrottleAsync(
            NatsJSMsg<byte[]> msg,
            SemaphoreSlim throttle,
            CancellationToken cancellationToken)
        {
            try
            {
                await ProcessMessageAsync(msg, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        }

        private async Task<INatsJSConsumer> CreateConsumerAsync(CancellationToken cancellationToken)
        {
            var spec = new DurableConsumerSpec(
                _deliveryOptions.WorkerName,
                _deliveryOptions.DurableName,
                _deliveryOptions.FilterSubject,
                _deliveryOptions.BatchSize,
                _deliveryOptions.MaxConcurrentMessages,
                _deliveryOptions.FetchTimeout,
                _deliveryOptions.NakDelay,
                _deliveryOptions.MaxAckPending,
                _deliveryOptions.AckWait,
                _deliveryOptions.MaxDeliver);

            var handle = await _consumerFactory.CreateOrUpdateAsync(spec, cancellationToken);
            return handle.Consumer;
        }

        private async Task ProcessMessageAsync(NatsJSMsg<byte[]> msg, CancellationToken cancellationToken)
        {
            using var activity = KairoTelemetry.ActivitySource.StartActivity("delivery.consume", ActivityKind.Consumer);
            activity?.SetTag(KairoTelemetry.WorkerTag, "delivery-dispatcher");

            DeliveryReady? ready;
            try
            {
                var json = msg.Data is { Length: > 0 } bytes ? Encoding.UTF8.GetString(bytes) : null;
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.Warning("Received empty delivery.ready message; terminating.");
                    await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                    TrackDeliveryWorkerOutcome("empty");
                    return;
                }
                ready = JsonSerializer.Deserialize<DeliveryReady>(json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deserialize delivery.ready payload; terminating message.");
                await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                TrackDeliveryWorkerOutcome("invalid_payload");
                return;
            }

            if (ready is null || ready.DeliveryId == Guid.Empty)
            {
                _logger.Warning("delivery.ready payload invalid; terminating message.");
                await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                TrackDeliveryWorkerOutcome("null_payload");
                return;
            }

            activity?.SetTag(KairoTelemetry.DeliveryIdTag, ready.DeliveryId.ToString("D"));
            activity?.SetTag(KairoTelemetry.EventIdTag, ready.EventId.ToString("D"));
            activity?.SetTag(KairoTelemetry.ContractIdTag, ready.ContractId.ToString("D"));
            activity?.SetTag(KairoTelemetry.CustomerIdTag, ready.CustomerId);
            WorkerMetrics.TrackJetStreamConsumerLag(DateTimeOffset.UtcNow - ready.OccurredAt, "delivery-dispatcher");
            TrackConsumerQueueState(msg, "delivery-dispatcher");

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDeliveryDispatcherService>();

                var result = await dispatcher.DispatchAsync(ready.DeliveryId, cancellationToken);

                if (result.IsSuccess)
                {
                    if (result.Value is DeliveryDispatchOutcome.FailedRetry or DeliveryDispatchOutcome.Skipped)
                    {
                        var delayed = await TryDelayRedeliveryAsync(
                            msg,
                            ready.DeliveryId,
                            scope.ServiceProvider,
                            cancellationToken);

                        if (!delayed)
                        {
                            await msg.AckAsync(cancellationToken: cancellationToken);
                            TrackDeliveryWorkerOutcome("acked");
                        }
                        else
                        {
                            TrackDeliveryWorkerOutcome("retry_delayed");
                        }
                    }
                    else
                    {
                        await msg.AckAsync(cancellationToken: cancellationToken);
                        TrackDeliveryWorkerOutcome("acked");
                    }
                }
                else
                {
                    if (await TryAckPoisonAsync(msg, ready.DeliveryId, cancellationToken))
                    {
                        TrackDeliveryWorkerOutcome("poison");
                        return;
                    }

                    _logger.Warning(
                        "Dispatcher failed for delivery {DeliveryId}: {Error}. NAK for redelivery.",
                        ready.DeliveryId, result.Error);
                    await msg.NakAsync(delay: _deliveryOptions.NakDelay, cancellationToken: cancellationToken);
                    TrackDeliveryWorkerOutcome("dispatcher_error");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try { await msg.NakAsync(delay: _deliveryOptions.NakDelay, cancellationToken: CancellationToken.None); }
                catch { /* best-effort */ }
                TrackDeliveryWorkerOutcome("cancelled");
                throw;
            }
            catch (Exception ex)
            {
                if (await TryAckPoisonAsync(msg, ready.DeliveryId, cancellationToken))
                {
                    TrackDeliveryWorkerOutcome("poison");
                    return;
                }

                _logger.Error(ex,
                    "Unexpected error dispatching delivery {DeliveryId}. NAK for redelivery.",
                    ready.DeliveryId);
                await msg.NakAsync(delay: _deliveryOptions.NakDelay, cancellationToken: cancellationToken);
                TrackDeliveryWorkerOutcome("error");
            }

            void TrackDeliveryWorkerOutcome(string outcome)
            {
                activity?.SetTag(KairoTelemetry.OutcomeTag, outcome);
                WorkerMetrics.TrackWorkerProcessed("delivery-dispatcher", outcome);
            }
        }

        private async Task<bool> TryAckPoisonAsync(
            NatsJSMsg<byte[]> msg,
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            var deliveryCount = GetMetadataULong(msg, "NumDelivered");
            if (deliveryCount is null)
                return false;

            if (deliveryCount.Value < (ulong)_deliveryOptions.MaxDeliver)
                return false;

            _logger.Warning(
                "Terminating poison delivery message {DeliveryId} after {Deliveries} deliveries.",
                deliveryId,
                deliveryCount.Value);
            await msg.AckTerminateAsync(cancellationToken: cancellationToken);
            return true;
        }

        private static void TrackConsumerQueueState(NatsJSMsg<byte[]> msg, string workerName)
        {
            var pending = GetMetadataULong(msg, "NumPending");
            if (pending is { } pendingCount)
                WorkerMetrics.TrackJetStreamConsumerPending((long)Math.Min(pendingCount, long.MaxValue), workerName);

            var delivered = GetMetadataULong(msg, "NumDelivered");
            if (delivered is { } deliveryCount)
                WorkerMetrics.TrackJetStreamConsumerDeliveryCount((long)Math.Min(deliveryCount, long.MaxValue), workerName);
        }

        private static ulong? GetMetadataULong(NatsJSMsg<byte[]> msg, string propertyName)
        {
            var metadata = msg.Metadata;
            if (metadata is null)
                return null;

            var property = metadata.GetType().GetProperty(propertyName);
            var value = property?.GetValue(metadata);
            return value switch
            {
                byte v => v,
                ushort v => v,
                uint v => v,
                ulong v => v,
                int v when v >= 0 => (ulong)v,
                long v when v >= 0 => (ulong)v,
                _ => null,
            };
        }

        private async Task<bool> TryDelayRedeliveryAsync(
            NatsJSMsg<byte[]> msg,
            Guid deliveryId,
            IServiceProvider scopeProvider,
            CancellationToken cancellationToken)
        {
            var repo = scopeProvider.GetRequiredService<IDeliveryRepository>();
            var load = await repo.GetByIdAsync(deliveryId, cancellationToken);
            if (load.IsFailure)
            {
                _logger.Warning(
                    "Failed to load delivery {DeliveryId} to compute retry delay; using default NAK delay.",
                    deliveryId);
                await msg.NakAsync(delay: _deliveryOptions.NakDelay, cancellationToken: cancellationToken);
                return true;
            }

            var delivery = load.Value;
            if (delivery is null)
            {
                return false;
            }

            if (delivery.Status is DeliveryStatus.Delivered)
            {
                return false;
            }

            if (delivery.Status is DeliveryStatus.Dead)
            {
                return false;
            }

            if (delivery.Status is DeliveryStatus.Failed && delivery.NextAttemptAt is null)
            {
                return false;
            }

            var delay = _deliveryOptions.NakDelay;
            if (delivery.NextAttemptAt is { } nextAttemptAt)
            {
                var computed = nextAttemptAt - DateTimeOffset.UtcNow;
                if (computed > TimeSpan.Zero)
                {
                    delay = computed;
                }
            }

            await msg.NakAsync(delay: delay, cancellationToken: cancellationToken);
            return true;
        }
    }
}

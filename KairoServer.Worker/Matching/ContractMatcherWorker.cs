using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Matching;
using KairoServer.Application.Observability;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Events;
using KairoServer.Infastructure.Messaging.Nats;
using KairoServer.Worker.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace KairoServer.Worker.Matching
{
    /// <summary>
    /// Consumes <c>event.accepted</c> messages from JetStream via a durable pull
    /// consumer, invokes <see cref="IContractMatcherService"/>, and ACKs only
    /// after the matcher commits. Multiple replicas can run concurrently: they
    /// share the same durable, so JetStream distributes messages across them
    /// with server-side <c>MaxAckPending</c> flow control.
    /// </summary>
    public sealed class ContractMatcherWorker : BackgroundService
    {
        private readonly NatsOption _natsOptions;
        private readonly MatcherOption _matcherOptions;
        private readonly IDurableConsumerFactory _consumerFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAppLogger<ContractMatcherWorker> _logger;

        public ContractMatcherWorker(
            NatsOption natsOptions,
            MatcherOption matcherOptions,
            IDurableConsumerFactory consumerFactory,
            IServiceScopeFactory scopeFactory,
            IAppLogger<ContractMatcherWorker> logger)
        {
            _natsOptions = natsOptions ?? throw new ArgumentNullException(nameof(natsOptions));
            _matcherOptions = matcherOptions ?? throw new ArgumentNullException(nameof(matcherOptions));
            _consumerFactory = consumerFactory ?? throw new ArgumentNullException(nameof(consumerFactory));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information(
                "ContractMatcherWorker starting (stream {Stream}, durable {Durable}, filter {Filter}, parallelism {Parallelism}).",
                _natsOptions.Stream, _matcherOptions.DurableName, _matcherOptions.FilterSubject, _matcherOptions.MaxConcurrentMessages);

            var consumer = await CreateConsumerAsync(stoppingToken);

            var fetchOpts = new NatsJSFetchOpts
            {
                MaxMsgs = _matcherOptions.BatchSize,
                Expires = _matcherOptions.FetchTimeout,
            };

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var throttle = new SemaphoreSlim(Math.Max(1, _matcherOptions.MaxConcurrentMessages));
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
                    _logger.Error(ex, "Unexpected error in ContractMatcherWorker fetch loop.");
                    // Prevent tight-loop on repeated failures.
                    try
                    {
                        await Task.Delay(_matcherOptions.NakDelay, stoppingToken);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }

            _logger.Information("ContractMatcherWorker stopped.");
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
                _matcherOptions.WorkerName,
                _matcherOptions.DurableName,
                _matcherOptions.FilterSubject,
                _matcherOptions.BatchSize,
                _matcherOptions.MaxConcurrentMessages,
                _matcherOptions.FetchTimeout,
                _matcherOptions.NakDelay,
                _matcherOptions.MaxAckPending,
                _matcherOptions.AckWait,
                _matcherOptions.MaxDeliver);

            var handle = await _consumerFactory.CreateOrUpdateAsync(spec, cancellationToken);
            return handle.Consumer;
        }

        private async Task ProcessMessageAsync(NatsJSMsg<byte[]> msg, CancellationToken cancellationToken)
        {
            using var activity = KairoTelemetry.ActivitySource.StartActivity("matcher.consume", ActivityKind.Consumer);
            activity?.SetTag(KairoTelemetry.WorkerTag, "contract-matcher");
            var stopwatch = Stopwatch.StartNew();

            EventAccepted? accepted;
            try
            {
                var json = msg.Data is { Length: > 0 } bytes
                    ? Encoding.UTF8.GetString(bytes)
                    : null;

                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.Warning("Received empty event.accepted message; discarding (term).");
                    await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                    TrackMatcherOutcome("empty");
                    return;
                }

                accepted = JsonSerializer.Deserialize<EventAccepted>(json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deserialize event.accepted payload; terminating message.");
                await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                TrackMatcherOutcome("invalid_payload");
                return;
            }

            if (accepted is null)
            {
                _logger.Warning("event.accepted deserialized to null; terminating message.");
                await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                TrackMatcherOutcome("null_payload");
                return;
            }

            activity?.SetTag(KairoTelemetry.EventIdTag, accepted.EventId.ToString("D"));
            WorkerMetrics.TrackJetStreamConsumerLag(DateTimeOffset.UtcNow - accepted.OccurredAt, "contract-matcher");
            TrackConsumerQueueState(msg, "contract-matcher");

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var matcher = scope.ServiceProvider.GetRequiredService<IContractMatcherService>();

                var result = await matcher.MatchAsync(accepted, cancellationToken);

                if (result.IsSuccess)
                {
                    await msg.AckAsync(cancellationToken: cancellationToken);
                    TrackMatcherOutcome("matched");
                }
                else
                {
                    if (await TryAckPoisonAsync(msg, accepted.EventId.ToString("D"), cancellationToken))
                    {
                        TrackMatcherOutcome("poison");
                        return;
                    }

                    _logger.Warning(
                        "Matcher failed for event {EventId}: {Error}. NAK for redelivery.",
                        accepted.EventId, result.Error);
                    await msg.NakAsync(delay: _matcherOptions.NakDelay, cancellationToken: cancellationToken);
                    TrackMatcherOutcome("retry");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Let JetStream redeliver on shutdown.
                try { await msg.NakAsync(delay: _matcherOptions.NakDelay, cancellationToken: CancellationToken.None); }
                catch { /* best-effort */ }
                TrackMatcherOutcome("cancelled");
                throw;
            }
            catch (Exception ex)
            {
                if (await TryAckPoisonAsync(msg, accepted.EventId.ToString("D"), cancellationToken))
                {
                    TrackMatcherOutcome("poison");
                    return;
                }

                _logger.Error(ex,
                    "Unexpected error while processing event {EventId}. NAK for redelivery.",
                    accepted.EventId);
                try { await msg.NakAsync(delay: _matcherOptions.NakDelay, cancellationToken: cancellationToken); }
                catch { /* best-effort */ }
                TrackMatcherOutcome("error");
            }

            void TrackMatcherOutcome(string outcome)
            {
                activity?.SetTag(KairoTelemetry.OutcomeTag, outcome);
                WorkerMetrics.TrackWorkerProcessed("contract-matcher", outcome);
                WorkerMetrics.TrackMatcherLatency(stopwatch.Elapsed, outcome);
            }
        }

        private async Task<bool> TryAckPoisonAsync(
            NatsJSMsg<byte[]> msg,
            string eventId,
            CancellationToken cancellationToken)
        {
            var deliveryCount = GetMetadataULong(msg, "NumDelivered");
            if (deliveryCount is null)
                return false;

            if (deliveryCount.Value < (ulong)_matcherOptions.MaxDeliver)
                return false;

            _logger.Warning(
                "Terminating poison matcher message for event {EventId} after {Deliveries} deliveries.",
                eventId,
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
    }
}

using KairoServer.Application.Messaging;
using KairoServer.Application.Observability;
using KairoServer.Application.Persistence;
using KairoServer.Common.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Worker.Observability;

namespace KairoServer.Worker.Outbox
{
    public sealed class OutboxPublisherWorker : BackgroundService
    {
        private readonly IOutboxReader _outbox;
        private readonly IEventBusPublisher _publisher;
        private readonly OutboxOption _options;
        private readonly IAppLogger<OutboxPublisherWorker> _logger;
        private readonly string _workerId;

        public OutboxPublisherWorker(
            IOutboxReader outbox,
            IEventBusPublisher publisher,
            OutboxOption options,
            IAppLogger<OutboxPublisherWorker> logger)
        {
            _outbox = outbox;
            _publisher = publisher;
            _options = options;
            _logger = logger;
            _workerId = $"outbox-{Environment.MachineName}-{Guid.NewGuid():N}";
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            _logger.Information(
                "OutboxPublisherWorker started (worker {WorkerId}, batch {BatchSize}, parallel {Parallelism}, claim ttl {ClaimTtl}).",
                _workerId,
                _options.BatchSize,
                _options.MaxConcurrentPublishes,
                _options.ClaimTtl);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var messagesResult = await _outbox.GetPendingAsync(
                        batchSize: _options.BatchSize,
                        workerId: _workerId,
                        claimTtl: _options.ClaimTtl,
                        stoppingToken);
                    var messages = messagesResult.EnsureValue();

                    if (messages.Count > 0)
                    {
                        _logger.Debug("Fetched {Count} pending outbox message(s).", messages.Count);
                    }

                    using var throttle = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentPublishes));
                    var publishTasks = new Task[messages.Count];

                    for (var i = 0; i < messages.Count; i++)
                    {
                        var message = messages[i];
                        publishTasks[i] = ProcessMessageAsync(message, throttle, stoppingToken);
                    }

                    await Task.WhenAll(publishTasks);

                    await Task.Delay(_options.PollDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unexpected error in OutboxPublisherWorker loop.");
                    await Task.Delay(_options.PollDelay, stoppingToken);
                }
            }

            await ReleaseWorkerClaimsAsync();

            _logger.Information("OutboxPublisherWorker stopped.");
        }

        private async Task ProcessMessageAsync(
            Domain.Outbox.OutboxMessage message,
            SemaphoreSlim throttle,
            CancellationToken cancellationToken)
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                using var activity = KairoTelemetry.ActivitySource.StartActivity("outbox.publish");
                activity?.SetTag(KairoTelemetry.EventIdTag, message.EventId.ToString("D"));

                try
                {
                    WorkerMetrics.TrackOutboxLag(DateTimeOffset.UtcNow - message.CreatedAt, message.Type);

                    var publishResult = await _publisher.PublishAsync(
                        message.Type,
                        message.Payload,
                        message.Id.ToString("D"),
                        cancellationToken);

                    publishResult.EnsureSuccess();

                    var markProcessedResult = await _outbox.MarkProcessedAsync(
                        message.Id,
                        _workerId,
                        cancellationToken);

                    markProcessedResult.EnsureSuccess();

                    _logger.Information(
                        "Published outbox message {OutboxId} (event {EventId}, type {Type}).",
                        message.Id,
                        message.EventId,
                        message.Type);

                    WorkerMetrics.TrackIngestionThroughput(message.Type);
                    WorkerMetrics.TrackWorkerProcessed("outbox-publisher", "published");
                    activity?.SetTag(KairoTelemetry.OutcomeTag, "published");
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        "Failed to publish outbox message {OutboxId} (event {EventId}, type {Type}, attempt {Attempt}): {Error}. Releasing claim and incrementing attempt count.",
                        message.Id,
                        message.EventId,
                        message.Type,
                        message.AttemptCount + 1,
                        ex.Message);

                    var releaseResult = await _outbox.ReleaseClaimAfterFailureAsync(
                        message.Id,
                        _workerId,
                        cancellationToken);

                    releaseResult.EnsureSuccess();
                    WorkerMetrics.TrackWorkerProcessed("outbox-publisher", "retry");
                    activity?.SetTag(KairoTelemetry.OutcomeTag, "retry");
                }
            }
            finally
            {
                throttle.Release();
            }
        }

        private async Task ReleaseWorkerClaimsAsync()
        {
            try
            {
                var release = await _outbox.ReleaseExpiredClaimsAsync(_workerId, CancellationToken.None);
                if (release.IsFailure)
                {
                    _logger.Warning("Failed to release outbox claims for worker {WorkerId}: {Error}", _workerId, release.Error);
                    return;
                }

                if (release.Value > 0)
                {
                    _logger.Information(
                        "Released {Count} pending outbox claim(s) for worker {WorkerId} during shutdown.",
                        release.Value,
                        _workerId);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to release outbox claims for worker {WorkerId}: {Error}", _workerId, ex.Message);
            }
        }
    }
}

using NuntivusServer.Application.Messaging;
using NuntivusServer.Application.Observability;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Worker.Observability;

namespace NuntivusServer.Worker.Outbox
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
                "OutboxPublisherWorker started (worker {WorkerId}, claimers {Claimers}, batch {BatchSize}, publish parallel {Parallelism}, claim ttl {ClaimTtl}).",
                _workerId,
                _options.ClaimWorkerCount,
                _options.BatchSize,
                _options.MaxConcurrentPublishes,
                _options.ClaimTtl);

            using var throttle = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentPublishes));
            var claimWorkers = new Task[Math.Max(1, _options.ClaimWorkerCount)];
            for (var i = 0; i < claimWorkers.Length; i++)
            {
                claimWorkers[i] = RunClaimLoopAsync(i + 1, throttle, stoppingToken);
            }

            await Task.WhenAll(claimWorkers);

            await ReleaseWorkerClaimsAsync();

            _logger.Information("OutboxPublisherWorker stopped.");
        }

        private async Task RunClaimLoopAsync(
            int claimerNumber,
            SemaphoreSlim throttle,
            CancellationToken stoppingToken)
        {
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
                        _logger.Debug("Claimer {ClaimerNumber} fetched {Count} pending outbox message(s).", claimerNumber, messages.Count);
                        WorkerMetrics.TrackOutboxClaimed(messages.Count, "outbox-publisher");
                    }

                    var batchResult = await ProcessBatchAsync(messages, throttle, stoppingToken);

                    if (messages.Count > 0)
                    {
                        _logger.Debug(
                            "Claimer {ClaimerNumber} processed batch size {BatchSize} (published {Published}, retry {Retry}, failed {Failed}).",
                            claimerNumber,
                            messages.Count,
                            batchResult.Published,
                            batchResult.Retry,
                            batchResult.Failed);
                    }

                    var delay = messages.Count == 0 ? _options.EmptyPollDelay : _options.PollDelay;
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unexpected error in OutboxPublisherWorker claim loop {ClaimerNumber}.", claimerNumber);
                    await Task.Delay(_options.PollDelay, stoppingToken);
                }
            }
        }

        private async Task<BatchResult> ProcessBatchAsync(
            IReadOnlyList<Domain.Outbox.OutboxMessage> messages,
            SemaphoreSlim throttle,
            CancellationToken cancellationToken)
        {
            var publishTasks = new Task<PublishOutcome>[messages.Count];
            for (var i = 0; i < messages.Count; i++)
            {
                publishTasks[i] = ProcessMessageAsync(messages[i], throttle, cancellationToken);
            }

            var outcomes = await Task.WhenAll(publishTasks);
            var published = 0;
            var retry = 0;
            var failed = 0;

            foreach (var outcome in outcomes)
            {
                switch (outcome)
                {
                    case PublishOutcome.Published:
                        published++;
                        break;
                    case PublishOutcome.Retry:
                        retry++;
                        break;
                    case PublishOutcome.Failed:
                        failed++;
                        break;
                }
            }

            return new BatchResult(published, retry, failed);
        }

        private async Task<PublishOutcome> ProcessMessageAsync(
            Domain.Outbox.OutboxMessage message,
            SemaphoreSlim throttle,
            CancellationToken cancellationToken)
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                using var activity = NuntivusTelemetry.ActivitySource.StartActivity("outbox.publish");
                activity?.SetTag(NuntivusTelemetry.EventIdTag, message.EventId.ToString("D"));

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
                    WorkerMetrics.TrackOutboxOutcome("published", message.Type);
                    WorkerMetrics.TrackWorkerProcessed("outbox-publisher", "published");
                    activity?.SetTag(NuntivusTelemetry.OutcomeTag, "published");
                    return PublishOutcome.Published;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var failedAt = DateTimeOffset.UtcNow;
                    var nextAttemptAt = CalculateNextAttemptAt(failedAt, message.AttemptCount);

                    _logger.Warning(
                        "Failed to publish outbox message {OutboxId} (event {EventId}, type {Type}, attempt {Attempt}): {Error}. Releasing claim and scheduling retry for {NextAttemptAt}.",
                        message.Id,
                        message.EventId,
                        message.Type,
                        message.AttemptCount + 1,
                        ex.Message,
                        nextAttemptAt);

                    try
                    {
                        var releaseResult = await _outbox.ReleaseClaimAfterFailureAsync(
                            message.Id,
                            _workerId,
                            failedAt,
                            nextAttemptAt,
                            ex.Message,
                            cancellationToken);

                        releaseResult.EnsureSuccess();
                        WorkerMetrics.TrackOutboxRetry(nextAttemptAt - failedAt, message.AttemptCount + 1, message.Type);
                        WorkerMetrics.TrackOutboxOutcome("retry", message.Type);
                        WorkerMetrics.TrackWorkerProcessed("outbox-publisher", "retry");
                        activity?.SetTag(NuntivusTelemetry.OutcomeTag, "retry");
                        return PublishOutcome.Retry;
                    }
                    catch (Exception releaseEx)
                    {
                        _logger.Error(
                            releaseEx,
                            "Failed to release and reschedule outbox message {OutboxId} for worker {WorkerId}.",
                            message.Id,
                            _workerId);

                        WorkerMetrics.TrackWorkerProcessed("outbox-publisher", "failed");
                        WorkerMetrics.TrackOutboxOutcome("failed", message.Type);
                        activity?.SetTag(NuntivusTelemetry.OutcomeTag, "failed");
                        return PublishOutcome.Failed;
                    }
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

        private DateTimeOffset CalculateNextAttemptAt(DateTimeOffset failedAt, int attemptCount)
        {
            var cappedAttempt = Math.Clamp(attemptCount, 0, 30);
            var exponent = Math.Pow(2, cappedAttempt);
            var baseDelay = TimeSpan.FromMilliseconds(_options.FailureBackoffBaseDelay.TotalMilliseconds * exponent);
            var maxDelay = _options.FailureBackoffMaxDelay;
            var boundedDelay = baseDelay <= maxDelay ? baseDelay : maxDelay;

            if (_options.FailureBackoffJitterRatio > 0)
            {
                var jitterSeconds = boundedDelay.TotalSeconds * _options.FailureBackoffJitterRatio;
                var jitter = (Random.Shared.NextDouble() * 2d - 1d) * jitterSeconds;
                boundedDelay = boundedDelay.Add(TimeSpan.FromSeconds(jitter));
                if (boundedDelay < TimeSpan.Zero)
                    boundedDelay = TimeSpan.Zero;
            }

            return failedAt.Add(boundedDelay);
        }

        private readonly record struct BatchResult(int Published, int Retry, int Failed);

        private enum PublishOutcome
        {
            Published,
            Retry,
            Failed,
        }
    }
}

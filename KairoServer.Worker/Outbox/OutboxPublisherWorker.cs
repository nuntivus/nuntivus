using KairoServer.Application.Messaging;
using KairoServer.Application.Persistence;
using KairoServer.Common.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KairoServer.Worker.Outbox
{
    public sealed class OutboxPublisherWorker : BackgroundService
    {
        private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(500);
        private const int BatchSize = 100;

        private readonly IOutboxReader _outbox;
        private readonly IEventBusPublisher _publisher;
        private readonly IAppLogger<OutboxPublisherWorker> _logger;

        public OutboxPublisherWorker(
            IOutboxReader outbox,
            IEventBusPublisher publisher,
            IAppLogger<OutboxPublisherWorker> logger)
        {
            _outbox = outbox;
            _publisher = publisher;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            _logger.Information("OutboxPublisherWorker started with batch size {BatchSize}.", BatchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var messagesResult = await _outbox.GetPendingAsync(
                        batchSize: BatchSize,
                        stoppingToken);
                    var messages = messagesResult.EnsureValue();

                    if (messages.Count > 0)
                    {
                        _logger.Debug("Fetched {Count} pending outbox message(s).", messages.Count);
                    }

                    foreach (var message in messages)
                    {
                        try
                        {
                            var publishResult = await _publisher.PublishAsync(
                                message.Type,
                                message.Payload,
                                message.EventId.ToString(),
                                stoppingToken);

                            publishResult.EnsureSuccess();

                            var markProcessedResult = await _outbox.MarkProcessedAsync(
                                message.Id,
                                stoppingToken);

                            markProcessedResult.EnsureSuccess();

                            _logger.Information(
                                "Published outbox message {OutboxId} (event {EventId}, type {Type}).",
                                message.Id,
                                message.EventId,
                                message.Type);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(
                                "Failed to publish outbox message {OutboxId} (event {EventId}, type {Type}, attempt {Attempt}): {Error}. Incrementing attempt count.",
                                message.Id,
                                message.EventId,
                                message.Type,
                                message.AttemptCount + 1,
                                ex.Message);

                            var incrementAttemptResult = await _outbox.IncrementAttemptAsync(
                                message.Id,
                                stoppingToken);

                            incrementAttemptResult.EnsureSuccess();
                        }
                    }

                    await Task.Delay(PollDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unexpected error in OutboxPublisherWorker loop.");
                    await Task.Delay(PollDelay, stoppingToken);
                }
            }

            _logger.Information("OutboxPublisherWorker stopped.");
        }
    }
}

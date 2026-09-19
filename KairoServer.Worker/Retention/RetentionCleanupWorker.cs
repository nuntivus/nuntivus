using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Retention;
using KairoServer.Common.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KairoServer.Worker.Retention
{
    public sealed class RetentionCleanupWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RetentionCleanupOption _options;
        private readonly IAppLogger<RetentionCleanupWorker> _logger;

        public RetentionCleanupWorker(
            IServiceScopeFactory scopeFactory,
            RetentionCleanupOption options,
            IAppLogger<RetentionCleanupWorker> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.Information("RetentionCleanupWorker disabled by configuration.");
                return;
            }

            _logger.Information(
                "RetentionCleanupWorker started (interval {Interval}, batchSize {BatchSize}, maxBatches {MaxBatches}).",
                _options.RunInterval,
                _options.BatchSize,
                _options.MaxBatchesPerCycle);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var cleanup = scope.ServiceProvider.GetRequiredService<IRetentionCleanupService>();

                    var result = await cleanup.RunCleanupCycleAsync(stoppingToken);
                    if (result.IsFailure)
                    {
                        _logger.Warning("Retention cleanup cycle failed: {Error}", result.Error?.Message ?? "unknown");
                    }
                    else if (result.Value.TotalDeletedRows > 0)
                    {
                        _logger.Information(
                            "Retention cleanup cycle removed {Total} rows (events {Events}, deliveries {Deliveries}, attempts {Attempts}, outbox {Outbox}, audits {Audits}) in {Batches} batches.",
                            result.Value.TotalDeletedRows,
                            result.Value.DeletedEvents,
                            result.Value.DeletedDeliveries,
                            result.Value.DeletedDeliveryAttempts,
                            result.Value.DeletedProcessedOutbox,
                            result.Value.DeletedAuditLogs,
                            result.Value.BatchesProcessed);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception in RetentionCleanupWorker loop.");
                }

                try
                {
                    await Task.Delay(_options.RunInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.Information("RetentionCleanupWorker stopped.");
        }
    }
}

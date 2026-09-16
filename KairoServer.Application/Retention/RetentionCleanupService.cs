using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;

namespace KairoServer.Application.Retention
{
    public sealed class RetentionCleanupService : IRetentionCleanupService
    {
        private readonly IRetentionCleanupRepository _repository;
        private readonly RetentionCleanupOption _options;
        private readonly IAppLogger<RetentionCleanupService> _logger;

        public RetentionCleanupService(
            IRetentionCleanupRepository repository,
            RetentionCleanupOption options,
            IAppLogger<RetentionCleanupService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<RetentionCleanupResult>> RunCleanupCycleAsync(CancellationToken cancellationToken)
        {
            if (!_options.Enabled)
            {
                return Result.Success(new RetentionCleanupResult(0, 0, 0, 0, 0));
            }

            if (_options.BatchSize <= 0)
                return Result.Failure<RetentionCleanupResult>(Error.Validation("RetentionCleanup:BatchSize must be > 0."));

            if (_options.MaxBatchesPerCycle <= 0)
                return Result.Failure<RetentionCleanupResult>(Error.Validation("RetentionCleanup:MaxBatchesPerCycle must be > 0."));

            try
            {
                var now = DateTimeOffset.UtcNow;

                var outbox = await DeleteInBatchesAsync(
                    (batchSize, ct) => _repository.DeleteProcessedOutboxBatchAsync(now - _options.ProcessedOutboxRetention, batchSize, ct),
                    "processed_outbox",
                    cancellationToken);

                var attempts = await DeleteInBatchesAsync(
                    (batchSize, ct) => _repository.DeleteDeliveryAttemptsBatchAsync(now - _options.DeliveryAttemptRetention, batchSize, ct),
                    "delivery_attempts",
                    cancellationToken);

                var deliveries = await DeleteInBatchesAsync(
                    (batchSize, ct) => _repository.DeleteDeliveriesBatchAsync(now - _options.DeliveryRetention, batchSize, ct),
                    "deliveries",
                    cancellationToken);

                var eventsDeleted = await DeleteInBatchesAsync(
                    (batchSize, ct) => _repository.DeleteEventsBatchAsync(now - _options.EventRetention, batchSize, ct),
                    "events",
                    cancellationToken);

                return Result.Success(new RetentionCleanupResult(
                    outbox.DeletedRows,
                    attempts.DeletedRows,
                    deliveries.DeletedRows,
                    eventsDeleted.DeletedRows,
                    outbox.Batches + attempts.Batches + deliveries.Batches + eventsDeleted.Batches));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Retention cleanup cycle failed.");
                return Result.Failure<RetentionCleanupResult>(ex);
            }
        }

        private async Task<DeleteBatchResult> DeleteInBatchesAsync(
            Func<int, CancellationToken, Task<Result<int>>> deleteBatch,
            string area,
            CancellationToken cancellationToken)
        {
            var totalDeleted = 0;
            var batches = 0;

            while (batches < _options.MaxBatchesPerCycle && !cancellationToken.IsCancellationRequested)
            {
                var result = await deleteBatch(_options.BatchSize, cancellationToken);
                var deleted = result.EnsureValue();

                batches++;
                totalDeleted += deleted;

                if (deleted < _options.BatchSize)
                    break;

                if (_options.InterBatchDelay > TimeSpan.Zero)
                    await Task.Delay(_options.InterBatchDelay, cancellationToken);
            }

            if (totalDeleted > 0)
            {
                _logger.Information("Retention cleanup removed {Count} row(s) from {Area} in {Batches} batch(es).", totalDeleted, area, batches);
            }

            return new DeleteBatchResult(totalDeleted, batches);
        }

        private readonly record struct DeleteBatchResult(int DeletedRows, int Batches);
    }
}

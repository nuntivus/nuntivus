using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common.Logging;
using Microsoft.Extensions.Hosting;

namespace KairoServer.Worker.Deliveries
{
    public sealed class DeliveryRecoveryWorker : BackgroundService
    {
        private readonly IDeliveryRepository _deliveries;
        private readonly DeliveryRecoveryOption _option;
        private readonly IAppLogger<DeliveryRecoveryWorker> _logger;

        public DeliveryRecoveryWorker(
            IDeliveryRepository deliveries,
            DeliveryRecoveryOption option,
            IAppLogger<DeliveryRecoveryWorker> logger)
        {
            _deliveries = deliveries;
            _option = option;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information(
                "DeliveryRecoveryWorker started (interval {Interval}, stale threshold {StaleAfter}, batch {BatchSize}).",
                _option.RunInterval,
                _option.StaleInFlightAfter,
                _option.BatchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var staleBefore = now - _option.StaleInFlightAfter;
                    var recovered = await _deliveries.RecoverStaleInFlightAsync(
                        staleBefore,
                        now,
                        _option.BatchSize,
                        stoppingToken);

                    recovered.EnsureSuccess();

                    if (recovered.Value > 0)
                    {
                        _logger.Warning(
                            "Recovered {Count} stale in-flight delivery row(s) older than {StaleBefore}.",
                            recovered.Value,
                            staleBefore);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "DeliveryRecoveryWorker iteration failed.");
                }

                try
                {
                    await Task.Delay(_option.RunInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.Information("DeliveryRecoveryWorker stopped.");
        }
    }
}

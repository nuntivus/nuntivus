using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Deliveries.Recovery;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NuntivusServer.Worker.Deliveries
{
    public sealed class DeliveryRecoveryWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DeliveryRecoveryOption _option;
        private readonly IAppLogger<DeliveryRecoveryWorker> _logger;

        public DeliveryRecoveryWorker(
            IServiceScopeFactory scopeFactory,
            DeliveryRecoveryOption option,
            IAppLogger<DeliveryRecoveryWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _option = option;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information(
                "DeliveryRecoveryWorker started (interval {Interval}, stale threshold {StaleAfter}, batch {BatchSize}, replay jobs/cycle {ReplayJobsPerCycle}).",
                _option.RunInterval,
                _option.StaleInFlightAfter,
                _option.BatchSize,
                _option.ReplayMaxJobsPerCycle);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var deliveries = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
                    var recovery = scope.ServiceProvider.GetRequiredService<IDeliveryRecoveryService>();

                    var now = DateTimeOffset.UtcNow;
                    var staleBefore = now - _option.StaleInFlightAfter;
                    var recovered = await deliveries.RecoverStaleInFlightAsync(
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

                    var replay = await recovery.ProcessReplayJobsAsync(_option.ReplayMaxJobsPerCycle, stoppingToken);
                    if (replay.IsFailure)
                    {
                        _logger.Warning("Replay recovery cycle failed: {Error}", replay.Error?.Message ?? "unknown");
                    }
                    else if (replay.Value.ClaimedJobs > 0)
                    {
                        _logger.Information(
                            "Processed {Claimed} replay job(s): completed {Completed}, failed {Failed}, replayed deliveries {Deliveries}.",
                            replay.Value.ClaimedJobs,
                            replay.Value.CompletedJobs,
                            replay.Value.FailedJobs,
                            replay.Value.TotalReplayedDeliveries);

                        if (_option.ReplayInterJobDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(_option.ReplayInterJobDelay, stoppingToken);
                        }
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

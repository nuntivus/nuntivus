using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Common.Logging;

namespace NuntivusServer.Worker.Retention
{
    public sealed class SloObservationRetentionWorker(IServiceScopeFactory scopes, IAppLogger<SloObservationRetentionWorker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var retention = scope.ServiceProvider.GetRequiredService<ISloObservationRetention>();
                    for (var batch = 0; batch < 20; batch++)
                    {
                        var result = await retention.DeleteExpiredBatchAsync(stoppingToken);
                        if (result.IsFailure) { logger.Warning("SLO observation retention failed: {Error}", result.Error?.Message); break; }
                        if (result.Value < 5000) break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.Error(ex, "SLO observation retention failed."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}

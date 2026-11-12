using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Common.Logging;

namespace NuntivusServer.Worker.Alerts
{
    public sealed class AlertWorker(IServiceScopeFactory scopes, IAppLogger<AlertWorker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();
                    var evaluated = await alerts.EvaluateAsync(stoppingToken);
                    if (evaluated.IsFailure) logger.Warning("Alert evaluation failed: {Error}", evaluated.Error?.Message);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.Error(ex, "Alert processing failed."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}

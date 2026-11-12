using Microsoft.Extensions.Hosting;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Common.Logging;

namespace NuntivusServer.Worker.Alerts
{
    public sealed class AlertWebhookWorker(IAlertWebhookQueue queue, IAlertNotificationSender sender, IAppLogger<AlertWebhookWorker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do
            {
                try
                {
                    for (var index = 0; index < 20; index++)
                    {
                        var claimed = await queue.ClaimAsync(stoppingToken);
                        if (claimed.IsFailure || claimed.Value is null) break;
                        var sent = await sender.SendAsync(claimed.Value, stoppingToken);
                        var completed = await queue.CompleteAsync(claimed.Value, sent.IsSuccess, stoppingToken);
                        if (completed.IsFailure) logger.Warning("Alert webhook completion failed: {Error}", completed.Error?.Message);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.Error(ex, "Alert webhook processing failed."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}

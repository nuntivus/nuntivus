using NuntivusServer.Common;
using NuntivusServer.Contracts.Alerts;

namespace NuntivusServer.Application.Alerts
{
    public interface IAlertOperationsService
    {
        Task<Result<AlertRuleView>> ArchiveRuleAsync(Guid id, string actorId, CancellationToken cancellationToken);
        Task<Result<AlertWebhookStatusView>> GetWebhookAsync(Guid notificationId, CancellationToken cancellationToken);
        Task<Result<PagedResult<AlertWebhookAttemptView>>> ListAttemptsAsync(Guid notificationId, int page, CancellationToken cancellationToken);
        Task<Result<AlertWebhookStatusView>> RetryWebhookAsync(Guid notificationId, AlertWebhookRetryRequest request, string actorId, CancellationToken cancellationToken);
    }
}

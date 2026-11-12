using NuntivusServer.Common;
using NuntivusServer.Contracts.Alerts;

namespace NuntivusServer.Application.Alerts
{
    public interface IAlertService
    {
        Task<Result<AlertRuleView>> SaveRuleAsync(Guid id, AlertRuleRequest request, string actorId, CancellationToken cancellationToken);
        Task<Result<PagedResult<AlertRuleView>>> ListRulesAsync(int page, CancellationToken cancellationToken);
        Task<Result<PagedResult<AlertNotificationView>>> ListNotificationsAsync(int page, CancellationToken cancellationToken);
        Task<Result<AlertNotificationView>> AcknowledgeAsync(Guid id, string actorId, CancellationToken cancellationToken);
        Task<Result<int>> EvaluateAsync(CancellationToken cancellationToken);
    }
    public sealed record AlertWebhookMessage(Guid NotificationId, Guid LeaseToken, string Endpoint, string? SecretRef, string Payload, int AttemptCount);
    public interface IAlertWebhookQueue
    {
        Task<Result<AlertWebhookMessage?>> ClaimAsync(CancellationToken cancellationToken);
        Task<Result> CompleteAsync(AlertWebhookMessage message, bool delivered, CancellationToken cancellationToken);
    }
    public interface IAlertNotificationSender
    {
        Task<Result> SendAsync(AlertWebhookMessage message, CancellationToken cancellationToken);
    }
}

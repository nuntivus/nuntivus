namespace NuntivusServer.Contracts.Alerts
{
    public sealed record AlertWebhookRetryRequest(Guid OperationId);
    public sealed class AlertWebhookStatusView
    {
        public Guid NotificationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public int CycleAttemptCount { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }
        public DateTimeOffset? LeasedUntil { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string? LastError { get; set; }
        public bool CanRetry { get; set; }
    }
    public sealed class AlertWebhookAttemptView
    {
        public int AttemptNumber { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public string? Error { get; set; }
    }
}

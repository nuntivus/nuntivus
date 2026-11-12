namespace NuntivusServer.Contracts.Alerts
{
    public sealed record AlertRuleRequest(Guid ContractId, string Name, string Signal, decimal Threshold,
        int ReminderSeconds = 0, bool Enabled = true, string? WebhookUrl = null, string? WebhookSecretRef = null);
    public sealed record AlertRuleView(Guid Id, Guid ContractId, string Name, string Signal, decimal Threshold,
        int ReminderSeconds, bool Enabled, bool Active, string? WebhookUrl, string? WebhookSecretRef, bool Archived = false);
    public sealed record AlertNotificationView(Guid Id, Guid RuleId, Guid ContractId, string State, string Message,
        DateTimeOffset CreatedAt, DateTimeOffset? AcknowledgedAt, string? AcknowledgedBy);
}

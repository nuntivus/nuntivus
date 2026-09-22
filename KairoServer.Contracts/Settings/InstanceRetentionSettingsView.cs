namespace KairoServer.Contracts.Settings
{
    public sealed record InstanceRetentionSettingsView(
        int EventRetentionDays,
        int DeliveryAuditRetentionDays,
        int OperationAuditRetentionDays);
}
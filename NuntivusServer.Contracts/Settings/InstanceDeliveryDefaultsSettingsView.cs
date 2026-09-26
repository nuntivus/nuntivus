namespace NuntivusServer.Contracts.Settings
{
    public sealed record InstanceDeliveryDefaultsSettingsView(
        int RequestTimeoutSeconds,
        int MaxAttempts,
        int BatchLimit);
}
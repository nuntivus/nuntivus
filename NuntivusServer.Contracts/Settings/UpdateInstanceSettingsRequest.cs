namespace NuntivusServer.Contracts.Settings
{
    public sealed record UpdateInstanceSettingsRequest(
        InstanceDeliveryDefaultsSettingsView DeliveryDefaults,
        InstanceRetryDefaultsSettingsView RetryDefaults,
        InstanceSecuritySettingsView Security,
        InstanceRetentionSettingsView Retention,
        InstanceLimitsSettingsView Limits);
}
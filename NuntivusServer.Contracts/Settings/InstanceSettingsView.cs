using System;

namespace NuntivusServer.Contracts.Settings
{
    public sealed record InstanceSettingsView(
        InstanceDeliveryDefaultsSettingsView DeliveryDefaults,
        InstanceRetryDefaultsSettingsView RetryDefaults,
        InstanceSecuritySettingsView Security,
        InstanceRetentionSettingsView Retention,
        InstanceLimitsSettingsView Limits,
        DateTimeOffset UpdatedAt,
        string? UpdatedBy);
}
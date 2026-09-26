namespace NuntivusServer.Contracts.Settings
{
    public sealed record InstanceRetryDefaultsSettingsView(
        int InitialDelaySeconds,
        int MaxDelaySeconds,
        double BackoffMultiplier);
}
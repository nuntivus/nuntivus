namespace NuntivusServer.Contracts.Settings
{
    public sealed record InstanceLimitsSettingsView(
        int MaxEventPayloadBytes,
        int MaxEventMetadataBytes,
        int MaxContractsPerCustomer,
        int MaxConcurrentReplayJobs);
}
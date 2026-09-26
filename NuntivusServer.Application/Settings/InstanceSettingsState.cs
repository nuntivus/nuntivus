using System;

namespace NuntivusServer.Application.Settings
{
    public sealed record InstanceSettingsState(
        int RequestTimeoutSeconds,
        int MaxAttempts,
        int BatchLimit,
        int InitialDelaySeconds,
        int MaxDelaySeconds,
        double BackoffMultiplier,
        bool RequireHttpsDestinations,
        bool AllowPrivateNetworkDestinations,
        bool RequireDestinationCredentialEncryption,
        int EventRetentionDays,
        int DeliveryAuditRetentionDays,
        int OperationAuditRetentionDays,
        int MaxEventPayloadBytes,
        int MaxEventMetadataBytes,
        int MaxContractsPerCustomer,
        int MaxConcurrentReplayJobs,
        DateTimeOffset UpdatedAt,
        string? UpdatedBy);
}
using System;
using System.Collections.Generic;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractCreated(
        Guid Id,
        string CustomerId,
        bool Enabled,
        IReadOnlyCollection<string> EventTypes,
        IReadOnlyCollection<ContractSchemaTargetView> SchemaTargets,
        ContractFilterView? Filter,
        ContractPayloadTransformationView? Transformation,
        ContractRateLimitConfig? RateLimit,
        ContractOrderingConfig? Ordering,
        DestinationType DestinationType,
        DateTimeOffset CreatedAt,
        string? Name = null,
        IReadOnlyCollection<Guid>? SourceIds = null,
        IReadOnlyCollection<Guid>? DestinationIds = null);
}

using System;
using System.Collections.Generic;
using NuntivusServer.Contracts.Destinations;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractView(
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
        string DestinationConfiguration,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? Name = null,
        IReadOnlyCollection<Guid>? SourceIds = null,
        IReadOnlyCollection<ContractDestinationView>? Destinations = null);
}

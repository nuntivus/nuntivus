using System;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Persistence
{
    /// <summary>
    /// Minimal projection returned by <see cref="IContractRepository.ListEnabledMatchingAsync"/>.
    /// Contains the identifiers and the destination snapshot the matcher needs to fan
    /// out deliveries, avoiding the cost of hydrating full <c>Contract</c> aggregates
    /// on the hot path. The destination is carried through so it can be denormalized
    /// onto each <c>Delivery</c> row at creation time.
    /// </summary>
    public sealed record ContractMatch(
        Guid ContractId,
        string CustomerId,
        Destination Destination,
        ContractFilter? Filter,
        ContractPayloadTransformation? Transformation = null,
        ContractRateLimit? RateLimit = null,
        ContractOrdering? Ordering = null,
        string? AuthenticationReference = null,
        int? AuthenticationVersion = null,
        int? TransformationVersion = null,
        int? SchemaVersion = null);
}

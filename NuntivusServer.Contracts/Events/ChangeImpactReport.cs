using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Contracts.Events
{
    public sealed record ImpactSample(Guid EventId, bool Accepted, IReadOnlyList<string> Findings);
    public sealed record ImpactBinding(Guid Id, Guid? DestinationId, string DestinationType, bool DeliveryEnabled);
    public sealed record ContractChangeImpact(Guid ContractId, string CustomerId, string? Name, bool Enabled,
        string Status, string Reason, int? OutputSchemaVersion, string? TransformationIdentity,
        IReadOnlyList<ImpactBinding> Bindings, IReadOnlyList<ImpactSample> Samples);
    public sealed record ChangeImpactReport(string EventType, int? ProposedSchemaVersion, Guid? ContractId,
        string? ProposedTransformationIdentity, DateTimeOffset GeneratedAt, int Page, int PageSize, long TotalContracts,
        IReadOnlyList<ContractChangeImpact> Contracts, string EvidenceNotice);
    public sealed record TransformationImpactRequest(string EventType, ContractPayloadTransformationRequest? Transformation,
        int SampleSize = 10);
}

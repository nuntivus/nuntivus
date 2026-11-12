using System.Text.Json;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractDryRunRequest(string EventType, Guid SourceId, JsonElement Payload,
        IReadOnlyDictionary<string, string>? Metadata = null, CreateContractRequest? Draft = null, bool DraftEnabled = true);
    public sealed record ContractDryRunStage(string Stage, string Status, string Message);
    public sealed record ContractDryRunDestination(Guid? BindingId, Guid? DestinationId, string Type, bool Enabled,
        string Status, string Message);
    public sealed record ContractDryRunReport(bool WouldDeliver, Guid? ContractId, string CustomerId,
        int? InputSchemaVersion, int? OutputSchemaVersion, string? TransformationIdentity,
        JsonElement? TransformedPayload, IReadOnlyList<ContractDryRunStage> Stages,
        IReadOnlyList<ContractDryRunDestination> Destinations, string Notice);
}

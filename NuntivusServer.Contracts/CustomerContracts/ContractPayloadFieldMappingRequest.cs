namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadFieldMappingRequest(
        string SourceSelector,
        string TargetSelector);
}

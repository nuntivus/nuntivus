namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadFieldMappingRequest(
        string SourceSelector,
        string TargetSelector);
}

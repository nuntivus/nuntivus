namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadFieldMappingView(
        string SourceSelector,
        string TargetSelector);
}

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadFieldMappingView(
        string SourceSelector,
        string TargetSelector);
}

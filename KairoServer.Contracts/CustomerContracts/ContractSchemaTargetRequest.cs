namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractSchemaTargetRequest(
        string EventType,
        int Version);
}
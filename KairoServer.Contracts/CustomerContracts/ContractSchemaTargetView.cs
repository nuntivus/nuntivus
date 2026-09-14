namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractSchemaTargetView(
        string EventType,
        int Version);
}
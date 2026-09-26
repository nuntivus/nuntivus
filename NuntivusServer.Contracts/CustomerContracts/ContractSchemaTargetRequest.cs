namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractSchemaTargetRequest(
        string EventType,
        int Version);
}
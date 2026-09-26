namespace NuntivusServer.Contracts.Settings
{
    public sealed record UpdateUserPreferencesRequest(
        string Theme,
        int DefaultPageSize,
        bool DenseTables);
}
namespace KairoServer.Contracts.CustomerContracts
{
    /// <summary>
    /// Unified credential model for destination adapters.
    /// <c>Kind="ref"</c> means <see cref="Value"/> is a secret/protected reference.
    /// <c>Kind="inline"</c> means <see cref="Value"/> is raw input that must be protected before persistence.
    /// </summary>
    public sealed record DestinationCredential(
        string Kind,
        string Value,
        int? Version = null);
}

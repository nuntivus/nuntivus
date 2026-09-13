using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    /// <summary>
    /// HTTP destination configuration payload for a contract.
    /// Authentication is expressed via <see cref="HttpAuthentication"/> and can
    /// carry non-secret headers (<see cref="Headers"/>) plus secret headers that
    /// are protected before persistence.
    /// </summary>
    public sealed record HttpDestinationConfig(
        string Url,
        HttpAuthentication Authentication,
        IReadOnlyDictionary<string, string>? Headers = null,
        IReadOnlyDictionary<string, string>? SecretHeaders = null,
        IReadOnlyDictionary<string, string>? ProtectedHeaders = null,
        int? TimeoutSeconds = null) : DestinationConfig;

    /// <summary>
    /// Authentication descriptor for an HTTP destination.
    /// <see cref="Credential"/> supports either a reference (e.g. <c>secret.acme.webhook</c>)
    /// or an inline value protected before persistence.
    /// </summary>
    public sealed record HttpAuthentication(
        string Type,
        HttpCredential? Credential = null);

    /// <summary>
    /// Unified credential model.
    /// <c>Kind="ref"</c> means <see cref="Value"/> is a reference
    /// (e.g. <c>secret.*</c> or <c>protected.*</c>).
    /// <c>Kind="inline"</c> means <see cref="Value"/> is raw input that must be
    /// protected before persistence.
    /// </summary>
    public sealed record HttpCredential(string Kind, string Value);
}

using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    /// <summary>
    /// HTTP destination configuration payload for a contract.
    /// </summary>
    public sealed record HttpDestinationConfig(
        string Url,
        HttpAuthentication Authentication,
        IReadOnlyDictionary<string, string>? Headers = null,
        int? TimeoutSeconds = null) : DestinationConfig;

    /// <summary>
    /// Authentication descriptor for an HTTP destination.
    /// <see cref="SecretRef"/> is a scheme-prefixed reference (e.g. <c>secret.acme.webhook</c>).
    /// </summary>
    public sealed record HttpAuthentication(string Type, string? SecretRef);
}

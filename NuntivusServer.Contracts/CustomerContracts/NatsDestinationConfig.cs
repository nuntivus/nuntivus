namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record NatsDestinationConfig(
        string Url,
        string Subject,
        int? TimeoutSeconds = null,
        bool UseJetStream = false,
        string? Stream = null,
        NatsCredentials? Credentials = null) : DestinationConfig;

    public sealed record NatsCredentials(
        DestinationCredential? Token = null,
        string? Username = null,
        DestinationCredential? Password = null);
}

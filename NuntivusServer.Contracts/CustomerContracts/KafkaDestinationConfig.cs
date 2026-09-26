namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record KafkaDestinationConfig(
        string BootstrapServers,
        string Topic,
        string? ClientId = null,
        string? Acks = null,
        int? TimeoutSeconds = null,
        KafkaCredentials? Credentials = null) : DestinationConfig;

    public sealed record KafkaCredentials(
        string? SecurityProtocol = null,
        string? SaslMechanism = null,
        string? Username = null,
        DestinationCredential? Password = null);
}

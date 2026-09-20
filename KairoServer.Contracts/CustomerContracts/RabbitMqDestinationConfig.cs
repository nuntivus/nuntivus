namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record RabbitMqDestinationConfig(
        string Uri,
        string Exchange,
        string RoutingKey,
        bool Persistent = true,
        bool Mandatory = false,
        int? TimeoutSeconds = null,
        RabbitMqCredentials? Credentials = null) : DestinationConfig;

    public sealed record RabbitMqCredentials(
        string? Username = null,
        DestinationCredential? Password = null);
}

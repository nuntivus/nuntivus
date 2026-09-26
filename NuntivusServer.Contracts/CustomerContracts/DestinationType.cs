namespace NuntivusServer.Contracts.CustomerContracts
{
    /// <summary>
    /// Transport-facing enumeration mirroring
    /// <c>NuntivusServer.Domain.Contracts.DestinationType</c>.
    /// </summary>
    public enum DestinationType
    {
        Http = 1,
        Kafka = 2,
        Nats = 3,
        RabbitMq = 4,
        Sqs = 5,
    }
}

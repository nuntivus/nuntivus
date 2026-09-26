namespace NuntivusServer.Domain.Contracts
{
    /// <summary>
    /// Delivery mechanism used by a Contract to hand off matched events to a Customer.
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

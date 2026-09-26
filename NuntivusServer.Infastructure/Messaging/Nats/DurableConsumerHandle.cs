using NATS.Client.JetStream;

namespace NuntivusServer.Infastructure.Messaging.Nats
{
    public sealed record DurableConsumerHandle(
        string DurableName,
        INatsJSConsumer Consumer);
}
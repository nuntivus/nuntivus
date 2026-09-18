using NATS.Client.JetStream;

namespace KairoServer.Infastructure.Messaging.Nats
{
    public sealed record DurableConsumerHandle(
        string DurableName,
        INatsJSConsumer Consumer);
}
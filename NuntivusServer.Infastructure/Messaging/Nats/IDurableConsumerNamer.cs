namespace NuntivusServer.Infastructure.Messaging.Nats
{
    public interface IDurableConsumerNamer
    {
        string GetDurableName(string workerName, string? configuredDurableName);
    }
}
using System;

namespace NuntivusServer.Infastructure.Messaging.Nats
{
    public sealed record DurableConsumerSpec(
        string WorkerName,
        string? DurableName,
        string FilterSubject,
        int BatchSize,
        int MaxConcurrentMessages,
        TimeSpan FetchTimeout,
        TimeSpan NakDelay,
        int MaxAckPending,
        TimeSpan AckWait,
        int MaxDeliver);
}
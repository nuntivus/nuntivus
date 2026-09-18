using System;

namespace KairoServer.Worker.Deliveries
{
    /// <summary>
    /// Configuration for the delivery dispatcher JetStream consumer loop. The
    /// durable is shared across replicas so JetStream distributes messages,
    /// enabling horizontal worker scaling.
    /// </summary>
    public sealed class DeliveryOption
    {
        public string WorkerName { get; set; } = "delivery-dispatcher";
        public string DurableName { get; set; } = "delivery-dispatcher";
        public string FilterSubject { get; set; } = "delivery.ready";
        public int BatchSize { get; set; } = 32;
        public int MaxConcurrentMessages { get; set; } = 8;
        public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan NakDelay { get; set; } = TimeSpan.FromSeconds(2);
        public int MaxAckPending { get; set; } = 500;
        public TimeSpan AckWait { get; set; } = TimeSpan.FromMinutes(2);
        public int MaxDeliver { get; set; } = 10;
    }
}

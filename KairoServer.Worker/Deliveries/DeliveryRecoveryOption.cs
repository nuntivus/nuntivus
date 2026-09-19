using System;

namespace KairoServer.Worker.Deliveries
{
    public sealed class DeliveryRecoveryOption
    {
        public TimeSpan RunInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan StaleInFlightAfter { get; set; } = TimeSpan.FromMinutes(5);
        public int BatchSize { get; set; } = 200;
        public int ReplayMaxJobsPerCycle { get; set; } = 5;
        public TimeSpan ReplayInterJobDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    }
}

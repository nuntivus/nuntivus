using System;

namespace NuntivusServer.Worker.Deliveries
{
    public sealed class DeliverySchedulerOption
    {
        public int BatchSize { get; set; } = 200;

        public TimeSpan PollDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        public TimeSpan EmptyPollDelay { get; set; } = TimeSpan.FromMilliseconds(750);
    }
}

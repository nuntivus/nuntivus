using System;

namespace KairoServer.Worker.Outbox
{
    public sealed class OutboxOption
    {
        public int BatchSize { get; set; } = 100;
        public TimeSpan PollDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public TimeSpan EmptyPollDelay { get; set; } = TimeSpan.FromMilliseconds(750);
        public int MaxConcurrentPublishes { get; set; } = 8;
        public int ClaimWorkerCount { get; set; } = 1;
        public TimeSpan ClaimTtl { get; set; } = TimeSpan.FromMinutes(2);
        public TimeSpan FailureBackoffBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan FailureBackoffMaxDelay { get; set; } = TimeSpan.FromMinutes(5);
        public double FailureBackoffJitterRatio { get; set; } = 0.2;
    }
}

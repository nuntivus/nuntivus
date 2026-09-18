using System;

namespace KairoServer.Worker.Outbox
{
    public sealed class OutboxOption
    {
        public int BatchSize { get; set; } = 100;
        public TimeSpan PollDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public int MaxConcurrentPublishes { get; set; } = 8;
        public TimeSpan ClaimTtl { get; set; } = TimeSpan.FromMinutes(2);
    }
}

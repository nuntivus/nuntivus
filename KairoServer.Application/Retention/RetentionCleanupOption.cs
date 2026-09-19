using System;

namespace KairoServer.Application.Retention
{
    public sealed class RetentionCleanupOption
    {
        public bool Enabled { get; set; } = true;
        public TimeSpan RunInterval { get; set; } = TimeSpan.FromMinutes(5);
        public int BatchSize { get; set; } = 500;
        public int MaxBatchesPerCycle { get; set; } = 20;
        public TimeSpan InterBatchDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        public TimeSpan EventRetention { get; set; } = TimeSpan.FromDays(30);
        public TimeSpan DeliveryRetention { get; set; } = TimeSpan.FromDays(14);
        public TimeSpan DeliveryAttemptRetention { get; set; } = TimeSpan.FromDays(14);
        public TimeSpan ProcessedOutboxRetention { get; set; } = TimeSpan.FromDays(7);
        public TimeSpan AuditLogRetention { get; set; } = TimeSpan.FromDays(90);
    }
}

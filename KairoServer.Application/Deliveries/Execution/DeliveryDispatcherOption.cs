using System;

namespace KairoServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Retry policy knobs applied by <see cref="DeliveryDispatcherService"/>
    /// when a destination executor returns a retryable failure category. The
    /// executor's <c>Retry-After</c> hint overrides <see cref="BaseBackoff"/>
    /// when present.
    /// </summary>
    public sealed class DeliveryDispatcherOption
    {
        /// <summary>Maximum total attempts before failure becomes terminal.</summary>
        public int MaxAttempts { get; set; } = 5;

        /// <summary>Initial backoff before doubling per attempt.</summary>
        public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Upper bound for computed backoff.</summary>
        public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
    }
}

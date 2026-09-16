using System;

namespace KairoServer.Application.Deliveries.Execution
{
    public sealed record DeliveryOrderingGateDecision(
        bool Granted,
        DateTimeOffset? RetryAfter = null)
    {
        public static DeliveryOrderingGateDecision Allow() => new(true, null);
        public static DeliveryOrderingGateDecision Block(DateTimeOffset? retryAfter = null) => new(false, retryAfter);
    }
}

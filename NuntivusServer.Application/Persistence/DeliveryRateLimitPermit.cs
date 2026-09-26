using System;

namespace NuntivusServer.Application.Persistence
{
    public sealed record DeliveryRateLimitPermit(
        bool Granted,
        DateTimeOffset? NextAllowedAt)
    {
        public static DeliveryRateLimitPermit Allow() => new(true, null);

        public static DeliveryRateLimitPermit DelayUntil(DateTimeOffset nextAllowedAt) =>
            new(false, nextAllowedAt);
    }
}
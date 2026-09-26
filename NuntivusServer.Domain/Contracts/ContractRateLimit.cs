using System;

namespace NuntivusServer.Domain.Contracts
{
    public sealed record ContractRateLimit(
        int? PerSecond,
        int? PerMinute,
        int Burst)
    {
        public bool IsEnabled => PerSecond is > 0 || PerMinute is > 0;

        public static ContractRateLimit? Create(
            int? perSecond,
            int? perMinute,
            int? burst)
        {
            if (perSecond is null && perMinute is null && burst is null)
                return null;

            if (perSecond is <= 0)
                throw new ArgumentOutOfRangeException(nameof(perSecond), "PerSecond must be greater than zero when provided.");

            if (perMinute is <= 0)
                throw new ArgumentOutOfRangeException(nameof(perMinute), "PerMinute must be greater than zero when provided.");

            var computedBurst = burst ?? perSecond ?? 1;
            if (computedBurst <= 0)
                throw new ArgumentOutOfRangeException(nameof(burst), "Burst must be greater than zero.");

            return new ContractRateLimit(perSecond, perMinute, computedBurst);
        }
    }
}
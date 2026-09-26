namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractRateLimitConfig(
        int? PerSecond = null,
        int? PerMinute = null,
        int? Burst = null);
}
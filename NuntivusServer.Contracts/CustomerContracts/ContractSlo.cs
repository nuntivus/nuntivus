namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractSloPolicyRequest(decimal SuccessTarget, decimal LatencyTarget,
        int LatencyThresholdSeconds, int SuccessDeadlineSeconds = 86400);
    public sealed record ContractSloBudget(long Eligible, long Good, long Bad, long Pending,
        decimal? AttainmentPercent, decimal AllowedFailures, decimal RemainingFailures, decimal? BudgetRemainingPercent);
    public sealed record ContractSloReport(Guid ContractId, ContractSloPolicyRequest Policy, DateTimeOffset MonitoringStartedAt,
        DateTimeOffset WindowStart, DateTimeOffset WindowEnd, bool FullWindowCovered,
        ContractSloBudget Success, ContractSloBudget Latency, long Excluded, string Notice);
}

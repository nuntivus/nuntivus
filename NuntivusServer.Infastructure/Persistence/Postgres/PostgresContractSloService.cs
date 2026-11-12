using System.Text.Json;
using Dapper;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Auditing;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresContractSloService(IDatabaseConnectionFactory connections, IContractRepository contracts,
        IAuditLogRepository audit, IUnitOfWork uow) : IContractSloService
    {
        public async Task<Result<ContractSloReport>> ConfigureAsync(Guid contractId, ContractSloPolicyRequest request, string actorId, CancellationToken cancellationToken)
        {
            if (request.SuccessTarget is <= 0 or > 100 || request.LatencyTarget is <= 0 or > 100
                || request.LatencyThresholdSeconds is < 1 or > 604800 || request.SuccessDeadlineSeconds is < 1 or > 604800
                || request.LatencyThresholdSeconds > request.SuccessDeadlineSeconds
                || decimal.Round(request.SuccessTarget, 5) != request.SuccessTarget || decimal.Round(request.LatencyTarget, 5) != request.LatencyTarget)
                return Result.Failure<ContractSloReport>(Error.Validation("Targets must be >0–100%; latency and success deadlines must be 1–604800 seconds, with latency no greater than the success deadline."));
            if (string.IsNullOrWhiteSpace(actorId)) return Result.Failure<ContractSloReport>(Error.Unauthorized("Operator identity is required."));
            var contract = await contracts.GetByIdAsync(contractId, cancellationToken);
            if (contract.IsFailure) return Result<ContractSloReport>.Fail(contract.Errors);
            if (contract.Value is null) return Result.Failure<ContractSloReport>(Error.NotFound("Contract was not found."));
            try
            {
                uow.Begin(true);
                await uow.GetConnection()!.ExecuteScalarAsync<Guid>(new CommandDefinition(
                    "SELECT id FROM contracts WHERE id = @ContractId FOR UPDATE", new { ContractId = contractId }, uow.GetTransaction(), cancellationToken: cancellationToken));
                var now = DateTimeOffset.UtcNow;
                var previous = await uow.GetConnection()!.QuerySingleOrDefaultAsync<PolicyRow>(new CommandDefinition("""
                    SELECT success_target AS SuccessTarget, latency_target AS LatencyTarget,
                        latency_threshold_seconds AS LatencyThresholdSeconds, success_deadline_seconds AS SuccessDeadlineSeconds,
                        monitoring_started_at AS MonitoringStartedAt FROM contract_slo_policies WHERE contract_id = @ContractId FOR UPDATE
                    """, new { ContractId = contractId }, uow.GetTransaction(), cancellationToken: cancellationToken));
                if (previous is not null && previous.SuccessTarget == request.SuccessTarget && previous.LatencyTarget == request.LatencyTarget
                    && previous.LatencyThresholdSeconds == request.LatencyThresholdSeconds && previous.SuccessDeadlineSeconds == request.SuccessDeadlineSeconds)
                {
                    uow.Commit();
                    return await GetAsync(contractId, cancellationToken);
                }
                await uow.GetConnection()!.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO contract_slo_policies (contract_id, success_target, latency_target, latency_threshold_seconds,
                        success_deadline_seconds, monitoring_started_at, updated_at)
                    VALUES (@ContractId, @SuccessTarget, @LatencyTarget, @LatencyThresholdSeconds, @SuccessDeadlineSeconds, @Now, @Now)
                    ON CONFLICT (contract_id) DO UPDATE SET success_target = EXCLUDED.success_target,
                        latency_target = EXCLUDED.latency_target, latency_threshold_seconds = EXCLUDED.latency_threshold_seconds,
                        success_deadline_seconds = EXCLUDED.success_deadline_seconds, monitoring_started_at = EXCLUDED.monitoring_started_at, updated_at = EXCLUDED.updated_at;
                    """, new { ContractId = contractId, request.SuccessTarget, request.LatencyTarget, request.LatencyThresholdSeconds, request.SuccessDeadlineSeconds, Now = now },
                    uow.GetTransaction(), cancellationToken: cancellationToken));
                (await audit.AddAsync(AuditLogEntry.Create("contract", "slo_configured", actorId, contract.Value.CustomerId, contractId,
                    null, null, null, JsonSerializer.Serialize(new { Request = request, Previous = previous,
                        Response = new { ContractId = contractId, Policy = request, MonitoringStartedAt = now } }), now, now.AddDays(90)), uow, cancellationToken)).EnsureSuccess();
                uow.Commit();
                return await GetAsync(contractId, cancellationToken);
            }
            catch (Exception ex) { uow.Rollback(); return Result.Failure<ContractSloReport>(ex); }
        }

        public async Task<Result<ContractSloReport>> GetAsync(Guid contractId, CancellationToken cancellationToken)
        {
            try
            {
                using var connection = connections.CreateConnection();
                var policy = await connection.QuerySingleOrDefaultAsync<PolicyRow>(new CommandDefinition("""
                    SELECT success_target AS SuccessTarget, latency_target AS LatencyTarget,
                        latency_threshold_seconds AS LatencyThresholdSeconds, success_deadline_seconds AS SuccessDeadlineSeconds,
                        monitoring_started_at AS MonitoringStartedAt FROM contract_slo_policies WHERE contract_id = @ContractId
                    """, new { ContractId = contractId }, cancellationToken: cancellationToken));
                if (policy is null) return Result.Failure<ContractSloReport>(Error.NotFound("No SLO policy is configured for this contract."));
                var now = DateTimeOffset.UtcNow;
                var rollingStart = now.AddDays(-30);
                var start = policy.MonitoringStartedAt > rollingStart ? policy.MonitoringStartedAt : rollingStart;
                var counts = await connection.QuerySingleAsync<CountRow>(new CommandDefinition("""
                    SELECT count(*) FILTER (WHERE excluded) AS Excluded,
                        count(*) FILTER (WHERE NOT excluded AND created_at + make_interval(secs => @SuccessDeadlineSeconds) <= @Now) AS SuccessEligible,
                        count(*) FILTER (WHERE NOT excluded AND created_at + make_interval(secs => @SuccessDeadlineSeconds) <= @Now
                            AND first_delivered_at <= created_at + make_interval(secs => @SuccessDeadlineSeconds)) AS SuccessGood,
                        count(*) FILTER (WHERE NOT excluded AND created_at + make_interval(secs => @SuccessDeadlineSeconds) > @Now) AS SuccessPending,
                        count(*) FILTER (WHERE NOT excluded AND created_at + make_interval(secs => @LatencyThresholdSeconds) <= @Now) AS LatencyEligible,
                        count(*) FILTER (WHERE NOT excluded AND created_at + make_interval(secs => @LatencyThresholdSeconds) <= @Now
                            AND first_delivered_at <= created_at + make_interval(secs => @LatencyThresholdSeconds)) AS LatencyGood,
                        count(*) FILTER (WHERE NOT excluded AND created_at + make_interval(secs => @LatencyThresholdSeconds) > @Now) AS LatencyPending
                    FROM delivery_slo_observations WHERE contract_id = @ContractId AND created_at >= @Start AND created_at < @Now
                    """, new { ContractId = contractId, Start = start, Now = now, policy.SuccessDeadlineSeconds, policy.LatencyThresholdSeconds }, cancellationToken: cancellationToken));
                return Result.Success(new ContractSloReport(contractId, new(policy.SuccessTarget, policy.LatencyTarget, policy.LatencyThresholdSeconds, policy.SuccessDeadlineSeconds),
                    policy.MonitoringStartedAt, start, now, policy.MonitoringStartedAt <= rollingStart,
                    Budget(counts.SuccessEligible, counts.SuccessGood, counts.SuccessPending, policy.SuccessTarget),
                    Budget(counts.LatencyEligible, counts.LatencyGood, counts.LatencyPending, policy.LatencyTarget), counts.Excluded,
                    "Rolling 30-day creation cohort, one observation per destination delivery. Only aged cohorts are scored; younger deliveries are pending. Tests and event replays excluded. Retries do not add observations; cancellation does not erase obligations. First success is preserved; late success cannot repair a missed deadline. Policy changes start a new monitoring period. No traffic is not 100% compliance."));
            }
            catch (Exception ex) { return Result.Failure<ContractSloReport>(ex); }
        }

        private static ContractSloBudget Budget(long eligible, long good, long pending, decimal target)
        {
            var bad = eligible - good;
            var allowed = eligible * (100m - target) / 100m;
            var remaining = allowed - bad;
            return new(eligible, good, bad, pending, eligible == 0 ? null : good * 100m / eligible,
                allowed, remaining, allowed == 0 ? null : remaining * 100m / allowed);
        }
        private sealed class PolicyRow
        {
            public decimal SuccessTarget { get; set; }
            public decimal LatencyTarget { get; set; }
            public int LatencyThresholdSeconds { get; set; }
            public int SuccessDeadlineSeconds { get; set; }
            public DateTimeOffset MonitoringStartedAt { get; set; }
        }
        private sealed class CountRow
        {
            public long Excluded { get; set; }
            public long SuccessEligible { get; set; }
            public long SuccessGood { get; set; }
            public long SuccessPending { get; set; }
            public long LatencyEligible { get; set; }
            public long LatencyGood { get; set; }
            public long LatencyPending { get; set; }
        }
    }
}

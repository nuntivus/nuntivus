using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresContractDeliveryRateLimiter : IContractDeliveryRateLimiter
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresContractDeliveryRateLimiter(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<Result<DeliveryRateLimitPermit>> TryAcquirePermitAsync(
            Guid contractId,
            ContractRateLimit rateLimit,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!rateLimit.IsEnabled)
                    return Result.Success(DeliveryRateLimitPermit.Allow());

                var perSecond = rateLimit.PerSecond ?? 0;
                var perMinute = rateLimit.PerMinute ?? 0;
                var burst = rateLimit.Burst;

                const string upsertSql = """
                    INSERT INTO contract_delivery_rate_limit_state
                    (
                        contract_id,
                        tokens,
                        last_refill_at,
                        minute_window_start,
                        minute_count,
                        updated_at
                    )
                    VALUES
                    (
                        @ContractId,
                        @Tokens,
                        @Now,
                        @MinuteWindowStart,
                        0,
                        @Now
                    )
                    ON CONFLICT (contract_id) DO NOTHING;
                    """;

                const string selectSql = """
                    SELECT
                        contract_id      AS ContractId,
                        tokens           AS Tokens,
                        last_refill_at   AS LastRefillAt,
                        minute_window_start AS MinuteWindowStart,
                        minute_count     AS MinuteCount
                    FROM contract_delivery_rate_limit_state
                    WHERE contract_id = @ContractId
                    FOR UPDATE;
                    """;

                const string updateSql = """
                    UPDATE contract_delivery_rate_limit_state
                    SET tokens = @Tokens,
                        last_refill_at = @LastRefillAt,
                        minute_window_start = @MinuteWindowStart,
                        minute_count = @MinuteCount,
                        updated_at = @UpdatedAt
                    WHERE contract_id = @ContractId;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                var minuteWindowStart = FloorToMinute(now);

                await connection.ExecuteAsync(new CommandDefinition(
                    upsertSql,
                    new
                    {
                        ContractId = contractId,
                        Tokens = (double)burst,
                        Now = now,
                        MinuteWindowStart = minuteWindowStart,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                var state = await connection.QuerySingleAsync<RateLimitStateRow>(new CommandDefinition(
                    selectSql,
                    new { ContractId = contractId },
                    transaction,
                    cancellationToken: cancellationToken));

                var tokens = state.Tokens;
                var lastRefillAt = state.LastRefillAt;
                var currentMinuteWindow = state.MinuteWindowStart;
                var minuteCount = state.MinuteCount;

                if (perSecond > 0)
                {
                    var elapsedSeconds = Math.Max(0d, (now - lastRefillAt).TotalSeconds);
                    tokens = Math.Min(burst, tokens + (elapsedSeconds * perSecond));
                    lastRefillAt = now;
                }
                else
                {
                    tokens = burst;
                    lastRefillAt = now;
                }

                if (perMinute > 0 && now >= currentMinuteWindow.AddMinutes(1))
                {
                    currentMinuteWindow = minuteWindowStart;
                    minuteCount = 0;
                }

                DateTimeOffset? secondGateAt = null;
                if (perSecond > 0 && tokens < 1d)
                {
                    var waitSeconds = (1d - tokens) / perSecond;
                    secondGateAt = now.AddSeconds(waitSeconds);
                }

                DateTimeOffset? minuteGateAt = null;
                if (perMinute > 0 && minuteCount >= perMinute)
                {
                    minuteGateAt = currentMinuteWindow.AddMinutes(1);
                }

                var granted = secondGateAt is null && minuteGateAt is null;
                if (granted)
                {
                    if (perSecond > 0)
                        tokens = Math.Max(0d, tokens - 1d);
                    if (perMinute > 0)
                        minuteCount++;
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    updateSql,
                    new
                    {
                        ContractId = contractId,
                        Tokens = tokens,
                        LastRefillAt = lastRefillAt,
                        MinuteWindowStart = currentMinuteWindow,
                        MinuteCount = minuteCount,
                        UpdatedAt = now,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                transaction.Commit();

                if (granted)
                    return Result.Success(DeliveryRateLimitPermit.Allow());

                var nextAllowedAt = MaxTimestamp(secondGateAt, minuteGateAt) ?? now.AddSeconds(1);
                return Result.Success(DeliveryRateLimitPermit.DelayUntil(nextAllowedAt));
            }
            catch (Exception ex)
            {
                return Result.Failure<DeliveryRateLimitPermit>(ex);
            }
        }

        private static DateTimeOffset FloorToMinute(DateTimeOffset value)
        {
            var utc = value.UtcDateTime;
            var flooredTicks = utc.Ticks - (utc.Ticks % TimeSpan.FromMinutes(1).Ticks);
            return new DateTimeOffset(flooredTicks, TimeSpan.Zero);
        }

        private static DateTimeOffset? MaxTimestamp(DateTimeOffset? a, DateTimeOffset? b)
        {
            if (a is null) return b;
            if (b is null) return a;
            return a > b ? a : b;
        }

        private sealed class RateLimitStateRow
        {
            public Guid ContractId { get; set; }
            public double Tokens { get; set; }
            public DateTimeOffset LastRefillAt { get; set; }
            public DateTimeOffset MinuteWindowStart { get; set; }
            public int MinuteCount { get; set; }
        }
    }
}
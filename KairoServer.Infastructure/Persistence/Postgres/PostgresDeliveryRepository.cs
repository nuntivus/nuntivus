using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresDeliveryRepository : IDeliveryRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresDeliveryRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<Result<int>> InsertPendingAsync(
            IReadOnlyCollection<Delivery> deliveries,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                if (deliveries.Count == 0)
                    return Result.Success(0);

                const string sql = """
                    INSERT INTO deliveries
                    (
                        id,
                        event_id,
                        contract_id,
                        customer_id,
                        event_type,
                        payload,
                        transformation_config,
                        rate_limit_per_second,
                        rate_limit_per_minute,
                        rate_limit_burst,
                        ordering_enabled,
                        ordering_key,
                        destination_type,
                        destination_config,
                        status,
                        attempt_count,
                        last_error,
                        next_attempt_at,
                        created_at,
                        updated_at
                    )
                    VALUES
                    (
                        @Id,
                        @EventId,
                        @ContractId,
                        @CustomerId,
                        @EventType,
                        CAST(@Payload AS jsonb),
                        CAST(@TransformationConfiguration AS jsonb),
                        @RateLimitPerSecond,
                        @RateLimitPerMinute,
                        @RateLimitBurst,
                        @OrderingEnabled,
                        @OrderingKey,
                        @DestinationType,
                        CAST(@DestinationConfiguration AS jsonb),
                        @Status,
                        @AttemptCount,
                        @LastError,
                        @NextAttemptAt,
                        @CreatedAt,
                        @UpdatedAt
                    )
                    ON CONFLICT (event_id, contract_id) DO NOTHING;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                var inserted = 0;
                foreach (var delivery in deliveries)
                {
                    var rows = await connection.ExecuteAsync(new CommandDefinition(
                        sql,
                        new
                        {
                            delivery.Id,
                            delivery.EventId,
                            delivery.ContractId,
                            delivery.CustomerId,
                            delivery.EventType,
                            delivery.Payload,
                            delivery.TransformationConfiguration,
                            RateLimitPerSecond = delivery.RateLimit?.PerSecond,
                            RateLimitPerMinute = delivery.RateLimit?.PerMinute,
                            RateLimitBurst = delivery.RateLimit?.Burst,
                            delivery.OrderingEnabled,
                            delivery.OrderingKey,
                            DestinationType = delivery.DestinationType.ToString(),
                            delivery.DestinationConfiguration,
                            Status = delivery.Status.ToString(),
                            delivery.AttemptCount,
                            delivery.LastError,
                            delivery.NextAttemptAt,
                            delivery.CreatedAt,
                            delivery.UpdatedAt,
                        },
                        transaction,
                        cancellationToken: cancellationToken));

                    inserted += rows;
                }

                return Result.Success(inserted);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
            }
        }

        public async Task<Result<Delivery?>> TryClaimForDispatchAsync(
            Guid deliveryId,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    UPDATE deliveries
                    SET status = @InFlightStatus,
                        attempt_count = attempt_count + 1,
                        next_attempt_at = NULL,
                        updated_at = @Now
                    WHERE id = @Id
                      AND updated_at = @ExpectedUpdatedAt
                      AND (
                            status = @PendingStatus
                            OR (
                                status = @FailedStatus
                                AND next_attempt_at IS NOT NULL
                                AND next_attempt_at <= @Now
                            )
                          )
                    RETURNING
                        id,
                        event_id AS EventId,
                        contract_id AS ContractId,
                        customer_id AS CustomerId,
                        event_type AS EventType,
                        payload::text AS Payload,
                        transformation_config::text AS TransformationConfig,
                        rate_limit_per_second AS RateLimitPerSecond,
                        rate_limit_per_minute AS RateLimitPerMinute,
                        rate_limit_burst AS RateLimitBurst,
                        ordering_enabled AS OrderingEnabled,
                        ordering_key AS OrderingKey,
                        destination_type AS DestinationType,
                        destination_config::text AS DestinationConfig,
                        status,
                        attempt_count AS AttemptCount,
                        last_error AS LastError,
                        next_attempt_at AS NextAttemptAt,
                        created_at AS CreatedAt,
                        updated_at AS UpdatedAt;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var row = await connection.QuerySingleOrDefaultAsync<DeliveryRow>(new CommandDefinition(
                    sql,
                    new
                    {
                        Id = deliveryId,
                        ExpectedUpdatedAt = expectedUpdatedAt,
                        Now = now,
                        PendingStatus = DeliveryStatus.Pending.ToString(),
                        FailedStatus = DeliveryStatus.Failed.ToString(),
                        InFlightStatus = DeliveryStatus.InFlight.ToString(),
                    },
                    cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<Delivery?>(null);

                return Result.Success<Delivery?>(MapDelivery(row));
            }
            catch (Exception ex)
            {
                return Result.Failure<Delivery?>(ex);
            }
        }

        public async Task<Result<bool>> ScheduleNextAttemptAsync(
            Guid deliveryId,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset nextAttemptAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    UPDATE deliveries
                    SET next_attempt_at = CASE
                        WHEN next_attempt_at IS NULL OR next_attempt_at < @NextAttemptAt
                            THEN @NextAttemptAt
                        ELSE next_attempt_at
                    END,
                    updated_at = @UpdatedAt
                    WHERE id = @Id
                      AND updated_at = @ExpectedUpdatedAt
                      AND status IN (@PendingStatus, @FailedStatus);
                    """;

                var rows = await uow.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Id = deliveryId,
                        NextAttemptAt = nextAttemptAt,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        ExpectedUpdatedAt = expectedUpdatedAt,
                        PendingStatus = DeliveryStatus.Pending.ToString(),
                        FailedStatus = DeliveryStatus.Failed.ToString(),
                    },
                    uow.GetTransaction(),
                    cancellationToken: cancellationToken));

                return Result.Success(rows == 1);
            }
            catch (Exception ex)
            {
                return Result.Failure<bool>(ex);
            }
        }

        public async Task<Result<int>> RecoverStaleInFlightAsync(
            DateTimeOffset staleBefore,
            DateTimeOffset now,
            int batchSize,
            CancellationToken cancellationToken)
        {
            try
            {
                if (batchSize <= 0)
                    return Result.Success(0);

                const string sql = """
                    WITH candidates AS (
                        SELECT id
                        FROM deliveries
                        WHERE status = @InFlightStatus
                          AND updated_at < @StaleBefore
                        ORDER BY updated_at ASC
                        LIMIT @BatchSize
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE deliveries d
                    SET status = @FailedStatus,
                        last_error = @RecoveryError,
                        next_attempt_at = @Now,
                        updated_at = @Now
                    FROM candidates c
                    WHERE d.id = c.id;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        StaleBefore = staleBefore,
                        BatchSize = batchSize,
                        Now = now,
                        InFlightStatus = DeliveryStatus.InFlight.ToString(),
                        FailedStatus = DeliveryStatus.Failed.ToString(),
                        RecoveryError = "Recovered stale in-flight delivery after worker interruption.",
                    },
                    cancellationToken: cancellationToken));

                return Result.Success(rows);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
            }
        }

        public async Task<Result<IReadOnlyList<DeliveryAttempt>>> GetAttemptsByDeliveryIdAsync(
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        id,
                        delivery_id      AS DeliveryId,
                        attempt_number   AS AttemptNumber,
                        succeeded,
                        failure_category AS FailureCategory,
                        error,
                        metadata::text   AS Metadata,
                        started_at       AS StartedAt,
                        completed_at     AS CompletedAt
                    FROM delivery_attempts
                    WHERE delivery_id = @DeliveryId
                    ORDER BY attempt_number;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var rows = await connection.QueryAsync<DeliveryAttemptRow>(new CommandDefinition(
                    sql,
                    new { DeliveryId = deliveryId },
                    cancellationToken: cancellationToken));

                var attempts = new List<DeliveryAttempt>();
                foreach (var row in rows)
                {
                    var metadata = string.IsNullOrWhiteSpace(row.Metadata)
                        ? null
                        : JsonSerializer.Deserialize<Dictionary<string, string>>(row.Metadata);

                    attempts.Add(DeliveryAttempt.Rehydrate(
                        row.Id,
                        row.DeliveryId,
                        row.AttemptNumber,
                        row.Succeeded,
                        row.FailureCategory,
                        row.Error,
                        metadata,
                        row.StartedAt,
                        row.CompletedAt));
                }

                return Result.Success<IReadOnlyList<DeliveryAttempt>>(attempts);
            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<DeliveryAttempt>>(ex);
            }
        }

        public async Task<Result<Delivery?>> GetByIdAsync(
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        id,
                        event_id            AS EventId,
                        contract_id         AS ContractId,
                        customer_id         AS CustomerId,
                        event_type          AS EventType,
                        payload::text       AS Payload,
                        transformation_config::text AS TransformationConfig,
                        rate_limit_per_second AS RateLimitPerSecond,
                        rate_limit_per_minute AS RateLimitPerMinute,
                        rate_limit_burst      AS RateLimitBurst,
                        ordering_enabled      AS OrderingEnabled,
                        ordering_key          AS OrderingKey,
                        destination_type    AS DestinationType,
                        destination_config::text AS DestinationConfig,
                        status,
                        attempt_count       AS AttemptCount,
                        last_error          AS LastError,
                        next_attempt_at     AS NextAttemptAt,
                        created_at          AS CreatedAt,
                        updated_at          AS UpdatedAt
                    FROM deliveries
                    WHERE id = @Id;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var row = await connection.QuerySingleOrDefaultAsync<DeliveryRow>(new CommandDefinition(
                    sql,
                    new { Id = deliveryId },
                    cancellationToken: cancellationToken));

                if (row is null)
                {
                    return Result.Success<Delivery?>(null);
                }

                var delivery = MapDelivery(row);

                return Result.Success<Delivery?>(delivery);
            }
            catch (Exception ex)
            {
                return Result.Failure<Delivery?>(ex);
            }
        }

        public async Task<Result<PagedResult<Delivery>>> ListDeadAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            try
            {
                if (page < 1)
                    return Result.Failure<PagedResult<Delivery>>("validation", "page must be greater than or equal to 1.");
                if (pageSize < 1)
                    return Result.Failure<PagedResult<Delivery>>("validation", "pageSize must be greater than or equal to 1.");

                const string sql = """
                    SELECT
                        id,
                        event_id            AS EventId,
                        contract_id         AS ContractId,
                        customer_id         AS CustomerId,
                        event_type          AS EventType,
                        payload::text       AS Payload,
                        transformation_config::text AS TransformationConfig,
                        rate_limit_per_second AS RateLimitPerSecond,
                        rate_limit_per_minute AS RateLimitPerMinute,
                        rate_limit_burst      AS RateLimitBurst,
                        ordering_enabled      AS OrderingEnabled,
                        ordering_key          AS OrderingKey,
                        destination_type    AS DestinationType,
                        destination_config::text AS DestinationConfig,
                        status,
                        attempt_count       AS AttemptCount,
                        last_error          AS LastError,
                        next_attempt_at     AS NextAttemptAt,
                        created_at          AS CreatedAt,
                        updated_at          AS UpdatedAt
                    FROM deliveries
                    WHERE status = @Status
                    ORDER BY updated_at DESC
                    OFFSET @Offset LIMIT @PageSize;

                    SELECT COUNT(*)
                    FROM deliveries
                    WHERE status = @Status;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Status = DeliveryStatus.Dead.ToString(),
                        Offset = (page - 1) * pageSize,
                        PageSize = pageSize,
                    },
                    cancellationToken: cancellationToken));

                var rows = (await multi.ReadAsync<DeliveryRow>()).AsList();
                var totalCount = await multi.ReadSingleAsync<long>();

                var mapped = new List<Delivery>(rows.Count);
                foreach (var row in rows)
                {
                    mapped.Add(MapDelivery(row));
                }

                return Result.Success(PagedResult<Delivery>.Create(mapped, page, pageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<Delivery>>(ex);
            }
        }

        public async Task<Result<bool>> UpdateAsync(
            Delivery delivery,
            DateTimeOffset expectedUpdatedAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    UPDATE deliveries
                    SET status          = @Status,
                        attempt_count   = @AttemptCount,
                        last_error      = @LastError,
                        next_attempt_at = @NextAttemptAt,
                        updated_at      = @UpdatedAt
                    WHERE id = @Id
                      AND updated_at = @ExpectedUpdatedAt;
                    """;

                var rows = await uow.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        delivery.Id,
                        Status = delivery.Status.ToString(),
                        delivery.AttemptCount,
                        delivery.LastError,
                        delivery.NextAttemptAt,
                        delivery.UpdatedAt,
                        ExpectedUpdatedAt = expectedUpdatedAt,
                    },
                    uow.GetTransaction(),
                    cancellationToken: cancellationToken));

                return Result.Success(rows == 1);
            }
            catch (Exception ex)
            {
                return Result.Failure<bool>(ex);
            }
        }

        public async Task<Result<int>> AddAttemptAsync(
            DeliveryAttempt attempt,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO delivery_attempts
                    (
                        id,
                        delivery_id,
                        attempt_number,
                        succeeded,
                        failure_category,
                        error,
                        metadata,
                        started_at,
                        completed_at
                    )
                    VALUES
                    (
                        @Id,
                        @DeliveryId,
                        @AttemptNumber,
                        @Succeeded,
                        @FailureCategory,
                        @Error,
                        CAST(@Metadata AS jsonb),
                        @StartedAt,
                        @CompletedAt
                    )
                    ON CONFLICT (delivery_id, attempt_number) DO NOTHING;
                    """;

                var rows = await uow.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        attempt.Id,
                        attempt.DeliveryId,
                        attempt.AttemptNumber,
                        attempt.Succeeded,
                        attempt.FailureCategory,
                        attempt.Error,
                        Metadata = JsonSerializer.Serialize(attempt.Metadata),
                        attempt.StartedAt,
                        attempt.CompletedAt,
                    },
                    uow.GetTransaction(),
                    cancellationToken: cancellationToken));

                return Result.Success(rows);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
            }
        }

        private sealed class DeliveryRow
        {
            public Guid Id { get; set; }
            public Guid EventId { get; set; }
            public Guid ContractId { get; set; }
            public string CustomerId { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
            public string? TransformationConfig { get; set; }
            public int? RateLimitPerSecond { get; set; }
            public int? RateLimitPerMinute { get; set; }
            public int? RateLimitBurst { get; set; }
            public bool OrderingEnabled { get; set; }
            public string? OrderingKey { get; set; }
            public string DestinationType { get; set; } = string.Empty;
            public string DestinationConfig { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int AttemptCount { get; set; }
            public string? LastError { get; set; }
            public DateTimeOffset? NextAttemptAt { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
        }

        private static Delivery MapDelivery(DeliveryRow row) =>
            Delivery.Rehydrate(
                row.Id,
                row.EventId,
                row.ContractId,
                row.CustomerId,
                row.EventType,
                row.Payload,
                row.TransformationConfig,
                ContractRateLimit.Create(row.RateLimitPerSecond, row.RateLimitPerMinute, row.RateLimitBurst),
                row.OrderingEnabled,
                row.OrderingKey,
                Enum.Parse<DestinationType>(row.DestinationType),
                row.DestinationConfig,
                Enum.Parse<DeliveryStatus>(row.Status),
                row.AttemptCount,
                row.LastError,
                row.NextAttemptAt,
                row.CreatedAt,
                row.UpdatedAt);

        private sealed class DeliveryAttemptRow
        {
            public Guid Id { get; set; }
            public Guid DeliveryId { get; set; }
            public int AttemptNumber { get; set; }
            public bool Succeeded { get; set; }
            public string? FailureCategory { get; set; }
            public string? Error { get; set; }
            public string? Metadata { get; set; }
            public DateTimeOffset StartedAt { get; set; }
            public DateTimeOffset CompletedAt { get; set; }
        }

    }
}

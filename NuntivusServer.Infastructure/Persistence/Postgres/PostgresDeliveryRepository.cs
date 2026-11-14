using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NuntivusServer.Application.Deliveries.Health;
using NuntivusServer.Application.Deliveries.Queries;
using NuntivusServer.Application.Deliveries.Recovery;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Domain.Deliveries;

namespace NuntivusServer.Infastructure.Persistence.Postgres
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
                        destination_binding_id,
                        customer_id,
                        event_type,
                        payload,
                        auth_ref,
                        auth_version,
                        transformation_version,
                        transformation_identity,
                        schema_version,
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
                        attempt_number_offset,
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
                        COALESCE(NULLIF(@DestinationBindingId, '00000000-0000-0000-0000-000000000000'::uuid),
                            (SELECT id FROM contract_destinations WHERE contract_id = @ContractId LIMIT 1)),
                        @CustomerId,
                        @EventType,
                        CAST(@Payload AS jsonb),
                        @AuthenticationReference,
                        @AuthenticationVersion,
                        @TransformationVersion,
                        @TransformationIdentity,
                        @SchemaVersion,
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
                        @AttemptNumberOffset,
                        @LastError,
                        @NextAttemptAt,
                        @CreatedAt,
                        @UpdatedAt
                    )
                    ON CONFLICT (event_id, contract_id, destination_binding_id) DO NOTHING;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                var inserted = 0;
                foreach (var delivery in deliveries)
                {
                    var secretValidation = await ManagedSecretReferenceValidator.ValidateAndLockAsync(
                        delivery.DestinationConfiguration, delivery.AuthenticationReference, uow, cancellationToken);
                    if (secretValidation.IsFailure)
                        return Result<int>.Fail(secretValidation.Errors);

                    var rows = await connection.ExecuteAsync(new CommandDefinition(
                        sql,
                        new
                        {
                            delivery.Id,
                            delivery.EventId,
                            delivery.ContractId,
                            delivery.DestinationBindingId,
                            delivery.CustomerId,
                            delivery.EventType,
                            delivery.Payload,
                            delivery.AuthenticationReference,
                            delivery.AuthenticationVersion,
                            delivery.TransformationVersion,
                            TransformationIdentity = string.IsNullOrWhiteSpace(delivery.TransformationConfiguration) ? null
                                : System.Text.Json.JsonSerializer.Deserialize<ContractPayloadTransformation>(delivery.TransformationConfiguration)?.Identity,
                            delivery.SchemaVersion,
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
                            delivery.AttemptNumberOffset,
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

        public async Task<Result<DeliveryQueryView?>> GetQueryByIdAsync(
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        d.id                     AS Id,
                        d.transformation_identity AS TransformationIdentity,
                        d.event_id               AS EventId,
                        d.customer_id            AS CustomerId,
                        d.contract_id            AS ContractId,
                        cd.destination_id        AS DestinationId,
                        d.destination_type       AS DestinationType,
                        d.event_type             AS EventType,
                        d.status                 AS Status,
                        d.attempt_count          AS AttemptCount,
                        d.last_error             AS LastError,
                        d.next_attempt_at        AS NextAttemptAt,
                        d.created_at             AS CreatedAt,
                        d.updated_at             AS UpdatedAt,
                        COALESCE(a.total_attempts, 0)      AS TotalAttempts,
                        COALESCE(a.successful_attempts, 0) AS SuccessfulAttempts,
                        COALESCE(a.failed_attempts, 0)     AS FailedAttempts,
                        a.last_attempt_at        AS LastAttemptAt,
                        lf.failure_category      AS LatestFailureCategory,
                        COALESCE(lf.error, d.last_error) AS LatestFailureError,
                        lf.completed_at          AS LatestFailureAt
                    FROM deliveries d
                    LEFT JOIN contract_destinations cd ON cd.id = d.destination_binding_id AND cd.contract_id = d.contract_id
                    LEFT JOIN LATERAL (
                        SELECT
                            COUNT(*)::int AS total_attempts,
                            COUNT(*) FILTER (WHERE succeeded)::int AS successful_attempts,
                            COUNT(*) FILTER (WHERE NOT succeeded)::int AS failed_attempts,
                            MAX(completed_at) AS last_attempt_at
                        FROM delivery_attempts da
                        WHERE da.delivery_id = d.id
                    ) a ON TRUE
                    LEFT JOIN LATERAL (
                        SELECT
                            failure_category,
                            error,
                            completed_at
                        FROM delivery_attempts da
                        WHERE da.delivery_id = d.id
                            AND da.succeeded = FALSE
                        ORDER BY da.attempt_number DESC
                        LIMIT 1
                    ) lf ON TRUE
                    WHERE d.id = @Id;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var row = await connection.QuerySingleOrDefaultAsync<DeliveryQueryRow>(new CommandDefinition(
                    sql,
                    new { Id = deliveryId },
                    cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<DeliveryQueryView?>(null);

                return Result.Success<DeliveryQueryView?>(MapDeliveryQuery(row));
            }
            catch (Exception ex)
            {
                return Result.Failure<DeliveryQueryView?>(ex);
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
                        destination_binding_id AS DestinationBindingId,
                        customer_id AS CustomerId,
                        event_type AS EventType,
                        payload::text AS Payload,
                        auth_ref AS AuthenticationReference,
                        auth_version AS AuthenticationVersion,
                        transformation_version AS TransformationVersion,
                        schema_version AS SchemaVersion,
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
                        attempt_number_offset AS AttemptNumberOffset,
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

        public async Task<Result<PagedResult<DeliveryQueryView>>> ListQueryAsync(
            DeliveryListQuery query,
            CancellationToken cancellationToken)
        {
            try
            {
                if (query.Page < 1)
                    return Result.Failure<PagedResult<DeliveryQueryView>>("validation", "page must be greater than or equal to 1.");
                if (query.PageSize < 1)
                    return Result.Failure<PagedResult<DeliveryQueryView>>("validation", "pageSize must be greater than or equal to 1.");

                var sortBy = string.IsNullOrWhiteSpace(query.SortBy)
                    ? "updatedat"
                    : query.SortBy.Trim().ToLowerInvariant();
                var sortDirection = string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                    ? "ASC"
                    : "DESC";

                var orderByColumn = sortBy switch
                {
                    "createdat" => "d.created_at",
                    _ => "d.updated_at",
                };

                var sql = $"""
                    SELECT
                        d.id                     AS Id,
                        d.transformation_identity AS TransformationIdentity,
                        d.event_id               AS EventId,
                        d.customer_id            AS CustomerId,
                        d.contract_id            AS ContractId,
                        cd.destination_id        AS DestinationId,
                        d.destination_type       AS DestinationType,
                        d.event_type             AS EventType,
                        d.status                 AS Status,
                        d.attempt_count          AS AttemptCount,
                        d.last_error             AS LastError,
                        d.next_attempt_at        AS NextAttemptAt,
                        d.created_at             AS CreatedAt,
                        d.updated_at             AS UpdatedAt,
                        COALESCE(a.total_attempts, 0)      AS TotalAttempts,
                        COALESCE(a.successful_attempts, 0) AS SuccessfulAttempts,
                        COALESCE(a.failed_attempts, 0)     AS FailedAttempts,
                        a.last_attempt_at        AS LastAttemptAt,
                        lf.failure_category      AS LatestFailureCategory,
                        COALESCE(lf.error, d.last_error) AS LatestFailureError,
                        lf.completed_at          AS LatestFailureAt
                    FROM deliveries d
                    LEFT JOIN contract_destinations cd ON cd.id = d.destination_binding_id AND cd.contract_id = d.contract_id
                    LEFT JOIN LATERAL (
                        SELECT
                            COUNT(*)::int AS total_attempts,
                            COUNT(*) FILTER (WHERE succeeded)::int AS successful_attempts,
                            COUNT(*) FILTER (WHERE NOT succeeded)::int AS failed_attempts,
                            MAX(completed_at) AS last_attempt_at
                        FROM delivery_attempts da
                        WHERE da.delivery_id = d.id
                    ) a ON TRUE
                    LEFT JOIN LATERAL (
                        SELECT
                            failure_category,
                            error,
                            completed_at
                        FROM delivery_attempts da
                        WHERE da.delivery_id = d.id
                            AND da.succeeded = FALSE
                        ORDER BY da.attempt_number DESC
                        LIMIT 1
                    ) lf ON TRUE
                    WHERE (@EventId IS NULL OR d.event_id = @EventId)
                        AND (@Search IS NULL OR d.id::text ILIKE @Search OR d.event_type ILIKE @Search)
                        AND (@CustomerId IS NULL OR d.customer_id = @CustomerId)
                        AND (@ContractId IS NULL OR d.contract_id = @ContractId)
                        AND (@DestinationType IS NULL OR d.destination_type = @DestinationType)
                        AND (
                            @FailureCategory IS NULL
                            OR EXISTS (
                                SELECT 1
                                FROM delivery_attempts daf
                                WHERE daf.delivery_id = d.id
                                    AND daf.succeeded = FALSE
                                    AND daf.failure_category = @FailureCategory
                            )
                        )
                        AND (@Status IS NULL OR d.status = @Status)
                        AND (@CreatedFrom IS NULL OR d.created_at >= @CreatedFrom)
                        AND (@CreatedTo IS NULL OR d.created_at <= @CreatedTo)
                    ORDER BY {orderByColumn} {sortDirection}, d.id DESC
                    OFFSET @Offset LIMIT @PageSize;

                    SELECT COUNT(*)
                    FROM deliveries d
                    WHERE (@EventId IS NULL OR d.event_id = @EventId)
                        AND (@Search IS NULL OR d.id::text ILIKE @Search OR d.event_type ILIKE @Search)
                        AND (@CustomerId IS NULL OR d.customer_id = @CustomerId)
                        AND (@ContractId IS NULL OR d.contract_id = @ContractId)
                        AND (@DestinationType IS NULL OR d.destination_type = @DestinationType)
                        AND (
                            @FailureCategory IS NULL
                            OR EXISTS (
                                SELECT 1
                                FROM delivery_attempts daf
                                WHERE daf.delivery_id = d.id
                                    AND daf.succeeded = FALSE
                                    AND daf.failure_category = @FailureCategory
                            )
                        )
                        AND (@Status IS NULL OR d.status = @Status)
                        AND (@CreatedFrom IS NULL OR d.created_at >= @CreatedFrom)
                        AND (@CreatedTo IS NULL OR d.created_at <= @CreatedTo);
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        query.EventId,
                        Search = string.IsNullOrWhiteSpace(query.Search) ? null : $"%{query.Search.Trim()}%",
                        CustomerId = string.IsNullOrWhiteSpace(query.CustomerId) ? null : query.CustomerId,
                        query.ContractId,
                        DestinationType = string.IsNullOrWhiteSpace(query.DestinationType) ? null : query.DestinationType,
                        FailureCategory = string.IsNullOrWhiteSpace(query.FailureCategory) ? null : query.FailureCategory,
                        Status = query.Status?.ToString(),
                        query.CreatedFrom,
                        query.CreatedTo,
                        Offset = (query.Page - 1) * query.PageSize,
                        query.PageSize,
                    },
                    cancellationToken: cancellationToken));

                var rows = (await multi.ReadAsync<DeliveryQueryRow>()).AsList();
                var totalCount = await multi.ReadSingleAsync<long>();

                var mapped = new List<DeliveryQueryView>(rows.Count);
                foreach (var row in rows)
                {
                    mapped.Add(MapDeliveryQuery(row));
                }

                return Result.Success(PagedResult<DeliveryQueryView>.Create(mapped, query.Page, query.PageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<DeliveryQueryView>>(ex);
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

        public async Task<Result<IReadOnlyList<Delivery>>> ClaimDueForSchedulingAsync(
            DateTimeOffset now,
            int batchSize,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                if (batchSize <= 0)
                    return Result.Success<IReadOnlyList<Delivery>>(Array.Empty<Delivery>());

                const string sql = """
                    WITH due AS (
                        SELECT id
                        FROM deliveries
                        WHERE next_attempt_at IS NOT NULL
                          AND next_attempt_at <= @Now
                          AND status = ANY(@EligibleStatuses)
                        ORDER BY next_attempt_at ASC, created_at ASC, id ASC
                        LIMIT @BatchSize
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE deliveries d
                    SET status = @PendingStatus,
                        next_attempt_at = NULL,
                        updated_at = @Now
                    FROM due
                    WHERE d.id = due.id
                    RETURNING
                        d.id,
                        d.event_id AS EventId,
                        d.contract_id AS ContractId,
                        d.destination_binding_id AS DestinationBindingId,
                        d.customer_id AS CustomerId,
                        d.event_type AS EventType,
                        d.payload::text AS Payload,
                        d.auth_ref AS AuthenticationReference,
                        d.auth_version AS AuthenticationVersion,
                        d.transformation_version AS TransformationVersion,
                        d.schema_version AS SchemaVersion,
                        d.transformation_config::text AS TransformationConfig,
                        d.rate_limit_per_second AS RateLimitPerSecond,
                        d.rate_limit_per_minute AS RateLimitPerMinute,
                        d.rate_limit_burst AS RateLimitBurst,
                        d.ordering_enabled AS OrderingEnabled,
                        d.ordering_key AS OrderingKey,
                        d.destination_type AS DestinationType,
                        d.destination_config::text AS DestinationConfig,
                        d.status,
                        d.attempt_count AS AttemptCount,
                        d.attempt_number_offset AS AttemptNumberOffset,
                        d.last_error AS LastError,
                        d.next_attempt_at AS NextAttemptAt,
                        d.created_at AS CreatedAt,
                        d.updated_at AS UpdatedAt;
                    """;

                var rows = await uow.GetConnection()!.QueryAsync<DeliveryRow>(new CommandDefinition(
                    sql,
                    new
                    {
                        Now = now,
                        BatchSize = batchSize,
                        PendingStatus = DeliveryStatus.Pending.ToString(),
                        EligibleStatuses = new[]
                        {
                            DeliveryStatus.Pending.ToString(),
                            DeliveryStatus.Failed.ToString(),
                        },
                    },
                    uow.GetTransaction(),
                    cancellationToken: cancellationToken));

                var deliveries = new List<Delivery>();
                foreach (var row in rows)
                {
                    deliveries.Add(MapDelivery(row));
                }

                return Result.Success<IReadOnlyList<Delivery>>(deliveries);
            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<Delivery>>(ex);
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

        public async Task<Result<DeliveryReplayJob?>> GetReplayJobByOperationIdAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        id,
                        operation_id AS OperationId,
                        operator_id AS OperatorId,
                        request_snapshot::text AS RequestSnapshot,
                        status,
                        event_type AS EventType,
                        customer_id AS CustomerId,
                        contract_id AS ContractId,
                        created_from AS CreatedFrom,
                        created_to AS CreatedTo,
                        batch_limit AS BatchLimit,
                        replayed_count AS ReplayedCount,
                        error,
                        created_at AS CreatedAt,
                        updated_at AS UpdatedAt,
                        completed_at AS CompletedAt
                    FROM delivery_replay_jobs
                    WHERE operation_id = @OperationId
                    LIMIT 1;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var row = await connection.QuerySingleOrDefaultAsync<DeliveryReplayJobRow>(new CommandDefinition(
                    sql,
                    new { OperationId = operationId },
                    cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<DeliveryReplayJob?>(null);

                return Result.Success<DeliveryReplayJob?>(MapDeliveryReplayJob(row));
            }
            catch (Exception ex)
            {
                return Result.Failure<DeliveryReplayJob?>(ex);
            }
        }

        public async Task<Result<DeliveryReplayJob?>> GetReplayJobByIdAsync(
            Guid replayJobId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        id,
                        operation_id AS OperationId,
                        operator_id AS OperatorId,
                        request_snapshot::text AS RequestSnapshot,
                        status,
                        event_type AS EventType,
                        customer_id AS CustomerId,
                        contract_id AS ContractId,
                        created_from AS CreatedFrom,
                        created_to AS CreatedTo,
                        batch_limit AS BatchLimit,
                        replayed_count AS ReplayedCount,
                        error,
                        created_at AS CreatedAt,
                        updated_at AS UpdatedAt,
                        completed_at AS CompletedAt
                    FROM delivery_replay_jobs
                    WHERE id = @Id
                    LIMIT 1;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var row = await connection.QuerySingleOrDefaultAsync<DeliveryReplayJobRow>(new CommandDefinition(
                    sql,
                    new { Id = replayJobId },
                    cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<DeliveryReplayJob?>(null);

                return Result.Success<DeliveryReplayJob?>(MapDeliveryReplayJob(row));
            }
            catch (Exception ex)
            {
                return Result.Failure<DeliveryReplayJob?>(ex);
            }
        }

        public async Task<Result<int>> AddReplayJobAsync(
            DeliveryReplayJob replayJob,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO delivery_replay_jobs
                    (
                        id,
                        operation_id,
                        operator_id,
                        request_snapshot,
                        status,
                        event_type,
                        customer_id,
                        contract_id,
                        created_from,
                        created_to,
                        batch_limit,
                        replayed_count,
                        error,
                        created_at,
                        updated_at,
                        completed_at
                    )
                    VALUES
                    (
                        @Id,
                        @OperationId,
                        @OperatorId,
                        CAST(@RequestSnapshot AS jsonb),
                        @Status,
                        @EventType,
                        @CustomerId,
                        @ContractId,
                        @CreatedFrom,
                        @CreatedTo,
                        @BatchLimit,
                        @ReplayedCount,
                        @Error,
                        @CreatedAt,
                        @UpdatedAt,
                        @CompletedAt
                    )
                    ON CONFLICT (operation_id) DO NOTHING;
                    """;

                var rows = await uow.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        replayJob.Id,
                        replayJob.OperationId,
                        replayJob.OperatorId,
                        replayJob.RequestSnapshot,
                        Status = replayJob.Status.ToString(),
                        replayJob.EventType,
                        replayJob.CustomerId,
                        replayJob.ContractId,
                        replayJob.CreatedFrom,
                        replayJob.CreatedTo,
                        replayJob.BatchLimit,
                        replayJob.ReplayedCount,
                        replayJob.Error,
                        replayJob.CreatedAt,
                        replayJob.UpdatedAt,
                        replayJob.CompletedAt,
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

        public async Task<Result<IReadOnlyList<DeliveryReplayJob>>> ClaimReplayJobsAsync(
            int maxJobs,
            CancellationToken cancellationToken)
        {
            try
            {
                if (maxJobs <= 0)
                    return Result.Success<IReadOnlyList<DeliveryReplayJob>>(Array.Empty<DeliveryReplayJob>());

                const string sql = """
                    WITH candidates AS (
                        SELECT id
                        FROM delivery_replay_jobs
                        WHERE status = @PendingStatus
                        ORDER BY created_at ASC
                        LIMIT @MaxJobs
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE delivery_replay_jobs j
                    SET status = @RunningStatus,
                        updated_at = @Now
                    FROM candidates c
                    WHERE j.id = c.id
                    RETURNING
                        j.id,
                        j.operation_id AS OperationId,
                        j.operator_id AS OperatorId,
                        j.request_snapshot::text AS RequestSnapshot,
                        j.status,
                        j.event_type AS EventType,
                        j.customer_id AS CustomerId,
                        j.contract_id AS ContractId,
                        j.created_from AS CreatedFrom,
                        j.created_to AS CreatedTo,
                        j.batch_limit AS BatchLimit,
                        j.replayed_count AS ReplayedCount,
                        j.error,
                        j.created_at AS CreatedAt,
                        j.updated_at AS UpdatedAt,
                        j.completed_at AS CompletedAt;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var rows = await connection.QueryAsync<DeliveryReplayJobRow>(new CommandDefinition(
                    sql,
                    new
                    {
                        MaxJobs = maxJobs,
                        PendingStatus = DeliveryReplayJobStatus.Pending.ToString(),
                        RunningStatus = DeliveryReplayJobStatus.Running.ToString(),
                        Now = DateTimeOffset.UtcNow,
                    },
                    cancellationToken: cancellationToken));

                var jobs = new List<DeliveryReplayJob>();
                foreach (var row in rows)
                {
                    jobs.Add(MapDeliveryReplayJob(row));
                }

                return Result.Success<IReadOnlyList<DeliveryReplayJob>>(jobs);
            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<DeliveryReplayJob>>(ex);
            }
        }

        public async Task<Result<int>> ExecuteReplayJobAsync(
            DeliveryReplayJob replayJob,
            CancellationToken cancellationToken)
        {
            try
            {
                if (replayJob.BatchLimit <= 0)
                    return Result.Success(0);

                const string sql = """
                    WITH candidates AS (
                        SELECT id
                        FROM deliveries
                        WHERE status = ANY(@ReplayableStatuses)
                          AND (@EventType IS NULL OR event_type = @EventType)
                          AND (@CustomerId IS NULL OR customer_id = @CustomerId)
                          AND (@ContractId IS NULL OR contract_id = @ContractId)
                          AND (@CreatedFrom IS NULL OR created_at >= @CreatedFrom)
                          AND (@CreatedTo IS NULL OR created_at <= @CreatedTo)
                        ORDER BY updated_at ASC, id ASC
                        LIMIT @BatchLimit
                        FOR UPDATE SKIP LOCKED
                    ), replayed AS (
                        UPDATE deliveries d
                        SET status = @FailedStatus,
                            next_attempt_at = @Now,
                            updated_at = @Now
                        FROM candidates c
                        WHERE d.id = c.id
                        RETURNING d.id
                    ), closed AS (
                        UPDATE delivery_slo_observations SET measurement_closed = true
                        WHERE delivery_id IN (SELECT id FROM replayed)
                        RETURNING delivery_id
                    )
                    SELECT count(*)::int FROM replayed;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var rows = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    sql,
                    new
                    {
                        ReplayableStatuses = new[]
                        {
                            DeliveryStatus.Failed.ToString(),
                            DeliveryStatus.Dead.ToString(),
                        },
                        EventType = replayJob.EventType,
                        CustomerId = replayJob.CustomerId,
                        ContractId = replayJob.ContractId,
                        CreatedFrom = replayJob.CreatedFrom,
                        CreatedTo = replayJob.CreatedTo,
                        BatchLimit = replayJob.BatchLimit,
                        FailedStatus = DeliveryStatus.Failed.ToString(),
                        Now = DateTimeOffset.UtcNow,
                    },
                    cancellationToken: cancellationToken));

                return Result.Success(rows);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
            }
        }

        public async Task<Result<bool>> CompleteReplayJobAsync(
            Guid replayJobId,
            DeliveryReplayJobStatus status,
            int replayedCount,
            string? error,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    UPDATE delivery_replay_jobs
                    SET status = @Status,
                        replayed_count = @ReplayedCount,
                        error = @Error,
                        updated_at = @Now,
                        completed_at = @Now
                    WHERE id = @Id
                      AND status = @RunningStatus;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Id = replayJobId,
                        Status = status.ToString(),
                        ReplayedCount = replayedCount,
                        Error = error,
                        Now = DateTimeOffset.UtcNow,
                        RunningStatus = DeliveryReplayJobStatus.Running.ToString(),
                    },
                    cancellationToken: cancellationToken));

                return Result.Success(rows == 1);
            }
            catch (Exception ex)
            {
                return Result.Failure<bool>(ex);
            }
        }

        public async Task<Result<DestinationHealthView?>> GetDestinationHealthByContractIdAsync(
            Guid contractId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        contract_id          AS ContractId,
                        customer_id          AS CustomerId,
                        destination_type     AS DestinationType,
                        consecutive_failures AS ConsecutiveFailures,
                        last_success_at      AS LastSuccessAt,
                        last_failure_at      AS LastFailureAt,
                        last_attempt_at      AS LastAttemptAt,
                        last_latency_ms      AS LastLatencyMs,
                        avg_latency_ms       AS AverageLatencyMs,
                        latency_sample_count AS LatencySampleCount,
                        health_status        AS Status,
                        updated_at           AS UpdatedAt
                    FROM destination_health
                    WHERE contract_id = @ContractId;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var row = await connection.QuerySingleOrDefaultAsync<DestinationHealthRow>(new CommandDefinition(
                    sql,
                    new { ContractId = contractId },
                    cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<DestinationHealthView?>(null);

                return Result.Success<DestinationHealthView?>(MapDestinationHealth(row));
            }
            catch (Exception ex)
            {
                return Result.Failure<DestinationHealthView?>(ex);
            }
        }

        public async Task<Result<IReadOnlyList<DestinationHealthView>>> ListDestinationHealthByCustomerIdAsync(
            string customerId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        contract_id          AS ContractId,
                        customer_id          AS CustomerId,
                        destination_type     AS DestinationType,
                        consecutive_failures AS ConsecutiveFailures,
                        last_success_at      AS LastSuccessAt,
                        last_failure_at      AS LastFailureAt,
                        last_attempt_at      AS LastAttemptAt,
                        last_latency_ms      AS LastLatencyMs,
                        avg_latency_ms       AS AverageLatencyMs,
                        latency_sample_count AS LatencySampleCount,
                        health_status        AS Status,
                        updated_at           AS UpdatedAt
                    FROM destination_health
                    WHERE customer_id = @CustomerId
                    ORDER BY
                        CASE health_status
                            WHEN 'Unhealthy' THEN 2
                            WHEN 'Degraded' THEN 1
                            ELSE 0
                        END DESC,
                        updated_at DESC,
                        contract_id;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var rows = await connection.QueryAsync<DestinationHealthRow>(new CommandDefinition(
                    sql,
                    new { CustomerId = customerId },
                    cancellationToken: cancellationToken));

                var result = new List<DestinationHealthView>();
                foreach (var row in rows)
                {
                    result.Add(MapDestinationHealth(row));
                }

                return Result.Success<IReadOnlyList<DestinationHealthView>>(result);
            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<DestinationHealthView>>(ex);
            }
        }

        public async Task<Result<DestinationHealthView>> UpsertDestinationHealthAsync(
            DestinationHealthObservation observation,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO destination_health
                    (
                        contract_id,
                        customer_id,
                        destination_type,
                        consecutive_failures,
                        last_success_at,
                        last_failure_at,
                        last_attempt_at,
                        last_latency_ms,
                        avg_latency_ms,
                        latency_sample_count,
                        health_status,
                        created_at,
                        updated_at
                    )
                    VALUES
                    (
                        @ContractId,
                        @CustomerId,
                        @DestinationType,
                        CASE WHEN @Succeeded THEN 0 ELSE 1 END,
                        CASE WHEN @Succeeded THEN @AttemptedAt ELSE NULL END,
                        CASE WHEN @Succeeded THEN NULL ELSE @AttemptedAt END,
                        @AttemptedAt,
                        @LatencyMs,
                        @LatencyMs,
                        CASE WHEN @LatencyMs IS NULL THEN 0 ELSE 1 END,
                        CASE
                            WHEN @Succeeded THEN 'Healthy'
                            ELSE 'Degraded'
                        END,
                        @Now,
                        @Now
                    )
                    ON CONFLICT (contract_id) DO UPDATE
                    SET
                        customer_id = EXCLUDED.customer_id,
                        destination_type = EXCLUDED.destination_type,
                        consecutive_failures = CASE
                            WHEN @Succeeded THEN 0
                            ELSE destination_health.consecutive_failures + 1
                        END,
                        last_success_at = CASE
                            WHEN @Succeeded THEN @AttemptedAt
                            ELSE destination_health.last_success_at
                        END,
                        last_failure_at = CASE
                            WHEN @Succeeded THEN destination_health.last_failure_at
                            ELSE @AttemptedAt
                        END,
                        last_attempt_at = @AttemptedAt,
                        last_latency_ms = CASE
                            WHEN @LatencyMs IS NULL THEN destination_health.last_latency_ms
                            ELSE @LatencyMs
                        END,
                        avg_latency_ms = CASE
                            WHEN @LatencyMs IS NULL THEN destination_health.avg_latency_ms
                            WHEN destination_health.latency_sample_count <= 0 OR destination_health.avg_latency_ms IS NULL THEN @LatencyMs
                            ELSE (
                                (destination_health.avg_latency_ms * destination_health.latency_sample_count)
                                + @LatencyMs
                            ) / (destination_health.latency_sample_count + 1)
                        END,
                        latency_sample_count = destination_health.latency_sample_count + CASE
                            WHEN @LatencyMs IS NULL THEN 0
                            ELSE 1
                        END,
                        health_status = CASE
                            WHEN (CASE
                                WHEN @Succeeded THEN 0
                                ELSE destination_health.consecutive_failures + 1
                            END) >= 3 THEN 'Unhealthy'
                            WHEN (CASE
                                WHEN @Succeeded THEN 0
                                ELSE destination_health.consecutive_failures + 1
                            END) >= 1 THEN 'Degraded'
                            ELSE 'Healthy'
                        END,
                        updated_at = @Now
                    RETURNING
                        contract_id          AS ContractId,
                        customer_id          AS CustomerId,
                        destination_type     AS DestinationType,
                        consecutive_failures AS ConsecutiveFailures,
                        last_success_at      AS LastSuccessAt,
                        last_failure_at      AS LastFailureAt,
                        last_attempt_at      AS LastAttemptAt,
                        last_latency_ms      AS LastLatencyMs,
                        avg_latency_ms       AS AverageLatencyMs,
                        latency_sample_count AS LatencySampleCount,
                        health_status        AS Status,
                        updated_at           AS UpdatedAt;
                    """;

                var now = DateTimeOffset.UtcNow;
                await uow.GetConnection()!.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO destination_failure_counters (contract_id, destination_binding_id, consecutive_failures, updated_at)
                    VALUES (@ContractId, @DestinationBindingId, CASE WHEN @Succeeded THEN 0 ELSE 1 END, @Now)
                    ON CONFLICT (contract_id, destination_binding_id) DO UPDATE
                    SET consecutive_failures = CASE WHEN @Succeeded THEN 0 ELSE destination_failure_counters.consecutive_failures + 1 END,
                        updated_at = @Now;
                    """, new { observation.ContractId, observation.DestinationBindingId, observation.Succeeded, Now = now },
                    uow.GetTransaction(), cancellationToken: cancellationToken));
                var row = await uow.GetConnection()!.QuerySingleAsync<DestinationHealthRow>(new CommandDefinition(
                    sql,
                    new
                    {
                        observation.ContractId,
                        observation.CustomerId,
                        observation.DestinationType,
                        observation.Succeeded,
                        observation.AttemptedAt,
                        observation.LatencyMs,
                        Now = now,
                    },
                    uow.GetTransaction(),
                    cancellationToken: cancellationToken));

                return Result.Success(MapDestinationHealth(row));
            }
            catch (Exception ex)
            {
                return Result.Failure<DestinationHealthView>(ex);
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

        public async Task<Result<DeliveryOperationAudit?>> GetOperationAuditAsync(
            Guid deliveryId,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        id,
                        delivery_id AS DeliveryId,
                        operation_id AS OperationId,
                        operation,
                        operator_id AS OperatorId,
                        request_snapshot::text AS RequestSnapshot,
                        response_snapshot::text AS ResponseSnapshot,
                        created_at AS CreatedAt
                    FROM delivery_operation_audits
                    WHERE delivery_id = @DeliveryId
                      AND operation_id = @OperationId;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var row = await connection.QuerySingleOrDefaultAsync<DeliveryOperationAuditRow>(new CommandDefinition(
                    sql,
                    new
                    {
                        DeliveryId = deliveryId,
                        OperationId = operationId,
                    },
                    cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<DeliveryOperationAudit?>(null);

                return Result.Success<DeliveryOperationAudit?>(MapDeliveryOperationAudit(row));
            }
            catch (Exception ex)
            {
                return Result.Failure<DeliveryOperationAudit?>(ex);
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
                        auth_ref            AS AuthenticationReference,
                        auth_version        AS AuthenticationVersion,
                        transformation_version AS TransformationVersion,
                        schema_version      AS SchemaVersion,
                        transformation_config::text AS TransformationConfig,
                        rate_limit_per_second AS RateLimitPerSecond,
                        rate_limit_per_minute AS RateLimitPerMinute,
                        rate_limit_burst      AS RateLimitBurst,
                        ordering_enabled      AS OrderingEnabled,
                        ordering_key          AS OrderingKey,
                        destination_type    AS DestinationType,
                        destination_config::text AS DestinationConfig,
                        destination_binding_id AS DestinationBindingId,
                        status,
                        attempt_count       AS AttemptCount,
                        attempt_number_offset AS AttemptNumberOffset,
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
                        auth_ref            AS AuthenticationReference,
                        auth_version        AS AuthenticationVersion,
                        transformation_version AS TransformationVersion,
                        schema_version      AS SchemaVersion,
                        transformation_config::text AS TransformationConfig,
                        rate_limit_per_second AS RateLimitPerSecond,
                        rate_limit_per_minute AS RateLimitPerMinute,
                        rate_limit_burst      AS RateLimitBurst,
                        ordering_enabled      AS OrderingEnabled,
                        ordering_key          AS OrderingKey,
                        destination_type    AS DestinationType,
                        destination_config::text AS DestinationConfig,
                        destination_binding_id AS DestinationBindingId,
                        status,
                        attempt_count       AS AttemptCount,
                        attempt_number_offset AS AttemptNumberOffset,
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
                        attempt_number_offset = @AttemptNumberOffset,
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
                        delivery.AttemptNumberOffset,
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

        public async Task<Result<int>> AddOperationAuditAsync(
            DeliveryOperationAudit operationAudit,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO delivery_operation_audits
                    (
                        id,
                        delivery_id,
                        operation_id,
                        operation,
                        operator_id,
                        request_snapshot,
                        response_snapshot,
                        created_at
                    )
                    VALUES
                    (
                        @Id,
                        @DeliveryId,
                        @OperationId,
                        @Operation,
                        @OperatorId,
                        CAST(@RequestSnapshot AS jsonb),
                        CAST(@ResponseSnapshot AS jsonb),
                        @CreatedAt
                    )
                    ON CONFLICT (delivery_id, operation_id) DO NOTHING;
                    """;

                var rows = await uow.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        operationAudit.Id,
                        operationAudit.DeliveryId,
                        operationAudit.OperationId,
                        operationAudit.Operation,
                        operationAudit.OperatorId,
                        operationAudit.RequestSnapshot,
                        operationAudit.ResponseSnapshot,
                        operationAudit.CreatedAt,
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
            public Guid DestinationBindingId { get; set; }
            public string CustomerId { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
            public string? AuthenticationReference { get; set; }
            public int? AuthenticationVersion { get; set; }
            public int? TransformationVersion { get; set; }
            public int? SchemaVersion { get; set; }
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
            public int AttemptNumberOffset { get; set; }
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
                row.AuthenticationReference,
                row.AuthenticationVersion,
                row.TransformationVersion,
                row.SchemaVersion,
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
                row.UpdatedAt,
                row.DestinationBindingId,
                row.AttemptNumberOffset);

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

        private sealed class DeliveryQueryRow
        {
            public Guid Id { get; set; }
            public string? TransformationIdentity { get; set; }
            public Guid EventId { get; set; }
            public string CustomerId { get; set; } = string.Empty;
            public Guid ContractId { get; set; }
            public Guid? DestinationId { get; set; }
            public string DestinationType { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int AttemptCount { get; set; }
            public string? LastError { get; set; }
            public DateTimeOffset? NextAttemptAt { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public int TotalAttempts { get; set; }
            public int SuccessfulAttempts { get; set; }
            public int FailedAttempts { get; set; }
            public DateTimeOffset? LastAttemptAt { get; set; }
            public string? LatestFailureCategory { get; set; }
            public string? LatestFailureError { get; set; }
            public DateTimeOffset? LatestFailureAt { get; set; }
        }

        private static DeliveryQueryView MapDeliveryQuery(DeliveryQueryRow row)
        {
            var latestFailure = row.LatestFailureError is null && row.LatestFailureCategory is null && row.LatestFailureAt is null
                ? null
                : new DeliveryLatestFailureView(row.LatestFailureCategory, row.LatestFailureError, row.LatestFailureAt);

            return new DeliveryQueryView(
                row.Id,
                row.EventId,
                row.CustomerId,
                row.ContractId,
                row.DestinationType,
                row.EventType,
                Enum.Parse<DeliveryStatus>(row.Status),
                row.AttemptCount,
                row.LastError,
                row.NextAttemptAt,
                row.CreatedAt,
                row.UpdatedAt,
                new DeliveryAttemptSummaryView(
                    row.TotalAttempts,
                    row.SuccessfulAttempts,
                    row.FailedAttempts,
                    row.LastAttemptAt),
                latestFailure,
                row.TransformationIdentity,
                row.DestinationId);
        }

        private sealed class DestinationHealthRow
        {
            public Guid ContractId { get; set; }
            public string CustomerId { get; set; } = string.Empty;
            public string DestinationType { get; set; } = string.Empty;
            public int ConsecutiveFailures { get; set; }
            public DateTimeOffset? LastSuccessAt { get; set; }
            public DateTimeOffset? LastFailureAt { get; set; }
            public DateTimeOffset LastAttemptAt { get; set; }
            public double? LastLatencyMs { get; set; }
            public double? AverageLatencyMs { get; set; }
            public long LatencySampleCount { get; set; }
            public string Status { get; set; } = string.Empty;
            public DateTimeOffset UpdatedAt { get; set; }
        }

        private static DestinationHealthView MapDestinationHealth(DestinationHealthRow row) =>
            new(
                row.ContractId,
                row.CustomerId,
                row.DestinationType,
                row.ConsecutiveFailures,
                row.LastSuccessAt,
                row.LastFailureAt,
                row.LastAttemptAt,
                row.LastLatencyMs,
                row.AverageLatencyMs,
                row.LatencySampleCount,
                Enum.Parse<DestinationHealthStatus>(row.Status),
                row.UpdatedAt);

        private sealed class DeliveryReplayJobRow
        {
            public Guid Id { get; set; }
            public Guid OperationId { get; set; }
            public string OperatorId { get; set; } = string.Empty;
            public string RequestSnapshot { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string? EventType { get; set; }
            public string? CustomerId { get; set; }
            public Guid? ContractId { get; set; }
            public DateTimeOffset? CreatedFrom { get; set; }
            public DateTimeOffset? CreatedTo { get; set; }
            public int BatchLimit { get; set; }
            public int ReplayedCount { get; set; }
            public string? Error { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public DateTimeOffset? CompletedAt { get; set; }
        }

        private sealed class DeliveryOperationAuditRow
        {
            public Guid Id { get; set; }
            public Guid DeliveryId { get; set; }
            public Guid OperationId { get; set; }
            public string Operation { get; set; } = string.Empty;
            public string OperatorId { get; set; } = string.Empty;
            public string RequestSnapshot { get; set; } = string.Empty;
            public string ResponseSnapshot { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
        }

        private static DeliveryOperationAudit MapDeliveryOperationAudit(DeliveryOperationAuditRow row) =>
            DeliveryOperationAudit.Rehydrate(
                row.Id,
                row.DeliveryId,
                row.OperationId,
                row.Operation,
                row.OperatorId,
                row.RequestSnapshot,
                row.ResponseSnapshot,
                row.CreatedAt);

        private static DeliveryReplayJob MapDeliveryReplayJob(DeliveryReplayJobRow row) =>
            DeliveryReplayJob.Rehydrate(
                row.Id,
                row.OperationId,
                row.OperatorId,
                row.RequestSnapshot,
                Enum.Parse<DeliveryReplayJobStatus>(row.Status),
                row.EventType,
                row.CustomerId,
                row.ContractId,
                row.CreatedFrom,
                row.CreatedTo,
                row.BatchLimit,
                row.ReplayedCount,
                row.Error,
                row.CreatedAt,
                row.UpdatedAt,
                row.CompletedAt);

    }
}

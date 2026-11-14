using Dapper;
using NuntivusServer.Application.Deliveries.Queries;
using NuntivusServer.Application.Events.Queries;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Deliveries;
using NuntivusServer.Domain.Events;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Npgsql;
namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public class PostgresEventRepository : IEventRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresEventRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Result> InsertAsync(
            Event @event,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO events
                    (
                        id,
                        idempotency_key,
                        type,
                        source_id,
                        occurred_at,
                        received_at,
                        metadata,
                        payload,
                        input_schema_version,
                        schema_validation_status
                    )
                    VALUES
                    (
                        @Id,
                        @IdempotencyKey,
                        @Type,
                        @SourceId,
                        @OccurredAt,
                        @ReceivedAt,
                        COALESCE(CAST(@Metadata AS jsonb), '{}'::jsonb),
                        CAST(@Payload AS jsonb),
                        @InputSchemaVersion,
                        @SchemaValidationStatus
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            @event.Id,
                            @event.IdempotencyKey,
                            @event.Type,
                            SourceId = Guid.Parse(@event.SourceId),
                            @event.OccurredAt,
                            @event.ReceivedAt,
                            Metadata = @event.Metadata?.ToJson(),
                            @event.Payload,
                            @event.InputSchemaVersion,
                            SchemaValidationStatus = @event.SchemaValidationStatus.ToString()
                        },
                        transaction,
                        cancellationToken: cancellationToken));

                return Result.Success();

            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                return Result.Failure(Error.Conflict(
                    $"Event with idempotency key '{@event.IdempotencyKey}' for source '{@event.SourceId}' already exists."));
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result<Event?>> GetByIngestionKeyAsync(
            string sourceId,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT id, idempotency_key, type, CAST(source_id AS uuid) AS source_id, occurred_at, received_at,
                           metadata::text AS metadata, payload::text AS payload,
                           input_schema_version, schema_validation_status
                    FROM events
                    WHERE source_id::text = @SourceId
                      AND idempotency_key = @IdempotencyKey
                    ORDER BY received_at
                    LIMIT 1;
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<EventRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            SourceId = sourceId,
                            IdempotencyKey = idempotencyKey
                        },
                        cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<Event?>(null);

                var metadata = row.metadata != null ? EventMetadata.Parse(row.metadata) : null;

                var @event = Event.Rehydrate(
                    row.id,
                    row.idempotency_key,
                    row.type,
                    row.source_id.ToString(),
                    row.occurred_at,
                    row.received_at,
                    row.payload,
                    metadata,
                    row.input_schema_version,
                    Enum.Parse<EventSchemaValidationStatus>(row.schema_validation_status));

                return Result.Success<Event?>(@event);
            }
            catch (Exception ex)
            {
                return Result.Failure<Event?>(ex);
            }
        }

        public async Task<Result<EventReplay?>> GetReplayAsync(
            Guid sourceEventId,
            string replayKey,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT source_event_id, replay_key, replayed_event_id, created_at
                    FROM event_replays
                    WHERE source_event_id = @SourceEventId
                      AND replay_key = @ReplayKey
                    LIMIT 1;
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<EventReplayRow>(
                    new CommandDefinition(
                        sql,
                        new { SourceEventId = sourceEventId, ReplayKey = replayKey },
                        cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<EventReplay?>(null);

                return Result.Success<EventReplay?>(new EventReplay(
                    row.source_event_id,
                    row.replay_key,
                    row.replayed_event_id,
                    row.created_at));
            }
            catch (Exception ex)
            {
                return Result.Failure<EventReplay?>(ex);
            }
        }

        public async Task<Result> InsertReplayAsync(
            EventReplay replay,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO event_replays
                    (
                        source_event_id,
                        replay_key,
                        replayed_event_id,
                        created_at
                    )
                    VALUES
                    (
                        @SourceEventId,
                        @ReplayKey,
                        @ReplayedEventId,
                        @CreatedAt
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        replay.SourceEventId,
                        replay.ReplayKey,
                        replay.ReplayedEventId,
                        replay.CreatedAt
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                return Result.Success();
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                return Result.Failure(Error.Conflict(
                    $"Replay '{replay.ReplayKey}' for event '{replay.SourceEventId}' already exists."));
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result<Event?>> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT id, idempotency_key, type, CAST(source_id AS uuid) AS source_id, occurred_at, received_at,
                           metadata::text AS metadata, payload::text AS payload,
                           input_schema_version, schema_validation_status
                    FROM events
                    WHERE id = @Id
                    LIMIT 1;
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<EventRow>(
                    new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<Event?>(null);

                var metadata = row.metadata != null ? EventMetadata.Parse(row.metadata) : null;

                var @event = Event.Rehydrate(
                    row.id,
                    row.idempotency_key,
                    row.type,
                    row.source_id.ToString(),
                    row.occurred_at,
                    row.received_at,
                    row.payload,
                    metadata,
                    row.input_schema_version,
                    Enum.Parse<EventSchemaValidationStatus>(row.schema_validation_status));

                return Result.Success<Event?>(@event);
            }
            catch (Exception ex)
            {
                return Result.Failure<Event?>(ex);
            }
        }

        public async Task<Result<EventQueryView?>> GetQueryByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        e.id                AS Id,
                        e.idempotency_key   AS IdempotencyKey,
                        e.type              AS Type,
                        e.source_id::text   AS Source,
                        e.occurred_at       AS OccurredAt,
                        e.received_at       AS ReceivedAt,
                        e.payload::text     AS Payload,
                        e.metadata::text    AS Metadata,
                        e.input_schema_version AS InputSchemaVersion,
                        e.schema_validation_status AS SchemaValidationStatus
                    FROM events e
                    WHERE e.id = @Id
                    LIMIT 1;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var row = await connection.QuerySingleOrDefaultAsync<EventQueryRow>(
                    new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<EventQueryView?>(null);

                var deliveriesResult = await LoadDeliveriesByEventIdsAsync(
                    connection,
                    new[] { id },
                    cancellationToken);
                if (deliveriesResult.IsFailure)
                    return Result<EventQueryView?>.Fail(deliveriesResult.Errors);

                var deliveriesByEventId = deliveriesResult.Value;
                var deliveries = deliveriesByEventId.TryGetValue(id, out var eventDeliveries)
                    ? eventDeliveries
                    : [];

                return Result.Success<EventQueryView?>(MapEventQuery(row, deliveries));
            }
            catch (Exception ex)
            {
                return Result.Failure<EventQueryView?>(ex);
            }
        }

        public async Task<Result<PagedResult<EventQueryView>>> ListQueryAsync(
            EventListQuery query,
            CancellationToken cancellationToken)
        {
            try
            {
                if (query.Page < 1)
                    return Result.Failure<PagedResult<EventQueryView>>("validation", "page must be greater than or equal to 1.");
                if (query.PageSize < 1)
                    return Result.Failure<PagedResult<EventQueryView>>("validation", "pageSize must be greater than or equal to 1.");

                var where = new StringBuilder(" WHERE 1 = 1");
                var parameters = new DynamicParameters();

                if (query.Id.HasValue)
                {
                    where.Append(" AND e.id = @Id");
                    parameters.Add("Id", query.Id.Value);
                }

                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    where.Append(" AND (e.id::text ILIKE @Search OR e.type ILIKE @Search OR e.idempotency_key ILIKE @Search)");
                    parameters.Add("Search", $"%{query.Search.Trim()}%");
                }

                if (!string.IsNullOrWhiteSpace(query.Type))
                {
                    where.Append(" AND e.type = @Type");
                    parameters.Add("Type", query.Type.Trim());
                }

                if (!string.IsNullOrWhiteSpace(query.Source))
                {
                    where.Append(" AND e.source_id::text = @Source");
                    parameters.Add("Source", query.Source.Trim());
                }

                if (query.OccurredFrom.HasValue)
                {
                    where.Append(" AND e.occurred_at >= @OccurredFrom");
                    parameters.Add("OccurredFrom", query.OccurredFrom.Value);
                }

                if (query.OccurredTo.HasValue)
                {
                    where.Append(" AND e.occurred_at <= @OccurredTo");
                    parameters.Add("OccurredTo", query.OccurredTo.Value);
                }

                if (query.ReceivedFrom.HasValue)
                {
                    where.Append(" AND e.received_at >= @ReceivedFrom");
                    parameters.Add("ReceivedFrom", query.ReceivedFrom.Value);
                }

                if (query.ReceivedTo.HasValue)
                {
                    where.Append(" AND e.received_at <= @ReceivedTo");
                    parameters.Add("ReceivedTo", query.ReceivedTo.Value);
                }

                var sortColumn = ResolveSortColumn(query.SortBy);
                var sortDirection = string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                    ? "ASC"
                    : "DESC";

                const string countSqlTemplate = "SELECT COUNT(*) FROM events e";
                var countSql = countSqlTemplate + where;

                parameters.Add("Offset", (query.Page - 1) * query.PageSize);
                parameters.Add("PageSize", query.PageSize);

                var pageSql = $"""
                    SELECT
                        e.id                AS Id,
                        e.idempotency_key   AS IdempotencyKey,
                        e.type              AS Type,
                        e.source_id::text   AS Source,
                        e.occurred_at       AS OccurredAt,
                        e.received_at       AS ReceivedAt,
                        e.payload::text     AS Payload,
                        e.metadata::text    AS Metadata,
                        e.input_schema_version AS InputSchemaVersion,
                        e.schema_validation_status AS SchemaValidationStatus
                    FROM events e
                    {where}
                    ORDER BY {sortColumn} {sortDirection}, e.id {sortDirection}
                    OFFSET @Offset
                    LIMIT @PageSize;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var totalCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    countSql,
                    parameters,
                    cancellationToken: cancellationToken));

                var rows = (await connection.QueryAsync<EventQueryRow>(new CommandDefinition(
                    pageSql,
                    parameters,
                    cancellationToken: cancellationToken))).ToList();

                if (rows.Count == 0)
                    return Result.Success(PagedResult<EventQueryView>.Empty(query.Page, query.PageSize));

                var eventIds = rows.Select(r => r.Id).ToArray();
                var deliveriesResult = await LoadDeliveriesByEventIdsAsync(connection, eventIds, cancellationToken);
                if (deliveriesResult.IsFailure)
                    return Result<PagedResult<EventQueryView>>.Fail(deliveriesResult.Errors);

                var deliveriesByEventId = deliveriesResult.Value;
                var mapped = rows.Select(row =>
                {
                    var deliveries = deliveriesByEventId.TryGetValue(row.Id, out var eventDeliveries)
                        ? eventDeliveries
                        : [];
                    return MapEventQuery(row, deliveries);
                }).ToArray();

                return Result.Success(PagedResult<EventQueryView>.Create(
                    mapped,
                    query.Page,
                    query.PageSize,
                    totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<EventQueryView>>(ex);
            }
        }

        private static string ResolveSortColumn(string? sortBy)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return "e.received_at";

            if (string.Equals(sortBy, "type", StringComparison.OrdinalIgnoreCase))
                return "e.type";
            if (string.Equals(sortBy, "source", StringComparison.OrdinalIgnoreCase))
                return "e.source_id";
            if (string.Equals(sortBy, "occurredAt", StringComparison.OrdinalIgnoreCase))
                return "e.occurred_at";

            return "e.received_at";
        }

        private async Task<Result<Dictionary<Guid, IReadOnlyList<DeliveryQueryView>>>> LoadDeliveriesByEventIdsAsync(
            System.Data.IDbConnection connection,
            IReadOnlyCollection<Guid> eventIds,
            CancellationToken cancellationToken)
        {
            try
            {
                if (eventIds.Count == 0)
                    return Result.Success(new Dictionary<Guid, IReadOnlyList<DeliveryQueryView>>());

                const string sql = """
                    SELECT
                        d.id                     AS Id,
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
                    WHERE d.event_id = ANY(@EventIds)
                    ORDER BY d.created_at DESC, d.id DESC;
                    """;

                var rows = (await connection.QueryAsync<DeliveryQueryRow>(new CommandDefinition(
                    sql,
                    new { EventIds = eventIds.ToArray() },
                    cancellationToken: cancellationToken))).ToList();

                var grouped = rows
                    .GroupBy(r => r.EventId)
                    .ToDictionary(
                        g => g.Key,
                        g => (IReadOnlyList<DeliveryQueryView>)g.Select(MapDeliveryQuery).ToArray());

                return Result.Success(grouped);
            }
            catch (Exception ex)
            {
                return Result.Failure<Dictionary<Guid, IReadOnlyList<DeliveryQueryView>>>(ex);
            }
        }

        private static EventQueryView MapEventQuery(
            EventQueryRow row,
            IReadOnlyList<DeliveryQueryView> deliveries) =>
            new(
                row.Id,
                row.IdempotencyKey,
                row.Type,
                row.Source,
                row.OccurredAt,
                row.ReceivedAt,
                row.Payload,
                row.Metadata,
                deliveries,
                row.InputSchemaVersion,
                row.SchemaValidationStatus);

        private sealed class EventQueryRow
        {
            public Guid Id { get; set; }
            public string IdempotencyKey { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
            public DateTimeOffset OccurredAt { get; set; }
            public DateTimeOffset ReceivedAt { get; set; }
            public string Payload { get; set; } = string.Empty;
            public string? Metadata { get; set; }
            public int? InputSchemaVersion { get; set; }
            public string SchemaValidationStatus { get; set; } = "Unknown";
        }

        private sealed class DeliveryQueryRow
        {
            public Guid Id { get; set; }
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
                DestinationId: row.DestinationId);
        }

        private sealed class EventRow
        {
            public Guid id { get; set; }
            public string idempotency_key { get; set; } = string.Empty;
            public string type { get; set; } = string.Empty;
            public Guid source_id { get; set; }
            public DateTimeOffset occurred_at { get; set; }
            public DateTimeOffset received_at { get; set; }
            public string? metadata { get; set; }
            public string payload { get; set; } = string.Empty;
            public int? input_schema_version { get; set; }
            public string schema_validation_status { get; set; } = "Unknown";
        }

        private sealed class EventReplayRow
        {
            public Guid source_event_id { get; set; }
            public string replay_key { get; set; } = string.Empty;
            public Guid replayed_event_id { get; set; }
            public DateTimeOffset created_at { get; set; }
        }
    }
}

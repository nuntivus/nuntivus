using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Events;
using Npgsql;
namespace KairoServer.Infastructure.Persistence.Postgres
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
                        payload
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
                        CAST(@Payload AS jsonb)
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
                            @event.Payload
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
                           metadata::text AS metadata, payload::text AS payload
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
                    metadata);

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
                           metadata::text AS metadata, payload::text AS payload
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
                    metadata);

                return Result.Success<Event?>(@event);
            }
            catch (Exception ex)
            {
                return Result.Failure<Event?>(ex);
            }
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

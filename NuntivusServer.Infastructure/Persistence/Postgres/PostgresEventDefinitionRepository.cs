using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Events;
using Npgsql;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresEventDefinitionRepository : IEventDefinitionRepository
    {
        private const string UniqueViolationSqlState = "23505";

        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresEventDefinitionRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Result> InsertAsync(
            EventDefinition definition,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO event_definitions
                    (
                        event_type,
                        display_name,
                        description,
                        created_at
                    )
                    VALUES
                    (
                        @EventType,
                        @DisplayName,
                        @Description,
                        @CreatedAt
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            definition.EventType,
                            definition.DisplayName,
                            definition.Description,
                            definition.CreatedAt
                        },
                        transaction,
                        cancellationToken: cancellationToken));

                return Result.Success();
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == UniqueViolationSqlState)
            {
                return Result.Failure(Error.Conflict(
                    $"Event definition for type '{definition.EventType}' already exists."));
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result<EventDefinition?>> GetByEventTypeAsync(
            string eventType,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT event_type, display_name, description, created_at
                    FROM event_definitions
                    WHERE event_type = @EventType
                    LIMIT 1;
                    """;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<EventDefinitionRow>(
                    new CommandDefinition(sql, new { EventType = eventType }, cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<EventDefinition?>(null);

                return Result.Success<EventDefinition?>(row.ToDomain());
            }
            catch (Exception ex)
            {
                return Result.Failure<EventDefinition?>(ex);
            }
        }

        public async Task<Result<PagedResult<EventDefinition>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT event_type, display_name, description, created_at,
                           COUNT(*) OVER() AS total_count
                    FROM event_definitions
                    ORDER BY created_at DESC, event_type ASC
                    LIMIT @PageSize OFFSET @Offset;
                    """;

                var offset = (page - 1) * pageSize;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var rows = await connection.QueryAsync<EventDefinitionRowWithCount>(
                    new CommandDefinition(
                        sql,
                        new { PageSize = pageSize, Offset = offset },
                        cancellationToken: cancellationToken));

                var materialized = new List<EventDefinition>();
                long totalCount = 0;
                foreach (var row in rows)
                {
                    totalCount = row.total_count;
                    materialized.Add(row.ToDomain());
                }

                return Result.Success(PagedResult<EventDefinition>.Create(materialized, page, pageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<EventDefinition>>(ex);
            }
        }

        public async Task<Result<int>> UpdateDescriptionAsync(
            string eventType,
            string? description,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    UPDATE event_definitions
                    SET description = @Description
                    WHERE event_type = @EventType;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        EventType = eventType,
                        Description = description,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                return Result.Success(rows);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
            }
        }

        public async Task<Result> InsertSchemaVersionAsync(
            EventSchemaVersion schemaVersion,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO event_schema_versions
                    (
                        event_type,
                        version_number,
                        schema_json,
                        compatibility_mode,
                        state_code,
                        created_at,
                        activated_at,
                        deprecated_at
                    )
                    VALUES
                    (
                        @EventType,
                        @Version,
                        CAST(@SchemaJson AS jsonb),
                        @CompatibilityMode,
                        @StateCode,
                        @CreatedAt,
                        @ActivatedAt,
                        @DeprecatedAt
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        schemaVersion.EventType,
                        schemaVersion.Version,
                        SchemaJson = schemaVersion.Schema,
                        CompatibilityMode = schemaVersion.CompatibilityMode.ToString(),
                        StateCode = schemaVersion.State.ToString(),
                        schemaVersion.CreatedAt,
                        schemaVersion.ActivatedAt,
                        schemaVersion.DeprecatedAt
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                return Result.Success();
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == UniqueViolationSqlState)
            {
                return Result.Failure(Error.Conflict(
                    $"Schema version '{schemaVersion.EventType}:{schemaVersion.Version}' already exists."));
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
            {
                return Result.Failure(Error.NotFound(
                    $"Event definition '{schemaVersion.EventType}' was not found."));
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result<EventSchemaVersion?>> GetSchemaVersionAsync(
            string eventType,
            int version,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT event_type,
                           version_number AS version,
                           schema_json::text AS schema,
                           compatibility_mode,
                           state_code AS state,
                           created_at,
                           activated_at,
                           deprecated_at
                    FROM event_schema_versions
                    WHERE event_type = @EventType
                      AND version_number = @Version
                    LIMIT 1;
                    """;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<EventSchemaVersionRow>(
                    new CommandDefinition(
                        sql,
                        new { EventType = eventType, Version = version },
                        cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<EventSchemaVersion?>(null);

                return Result.Success<EventSchemaVersion?>(row.ToDomain());
            }
            catch (Exception ex)
            {
                return Result.Failure<EventSchemaVersion?>(ex);
            }
        }

        public async Task<Result<PagedResult<EventSchemaVersion>>> ListSchemaVersionsAsync(
            string eventType,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT event_type,
                           version_number AS version,
                           schema_json::text AS schema,
                           compatibility_mode,
                           state_code AS state,
                           created_at,
                           activated_at,
                           deprecated_at,
                           COUNT(*) OVER() AS total_count
                    FROM event_schema_versions
                    WHERE event_type = @EventType
                    ORDER BY version_number DESC
                    LIMIT @PageSize OFFSET @Offset;
                    """;

                var offset = (page - 1) * pageSize;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var rows = await connection.QueryAsync<EventSchemaVersionRowWithCount>(
                    new CommandDefinition(
                        sql,
                        new { EventType = eventType, PageSize = pageSize, Offset = offset },
                        cancellationToken: cancellationToken));

                var materialized = new List<EventSchemaVersion>();
                long totalCount = 0;
                foreach (var row in rows)
                {
                    totalCount = row.total_count;
                    materialized.Add(row.ToDomain());
                }

                return Result.Success(PagedResult<EventSchemaVersion>.Create(materialized, page, pageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<EventSchemaVersion>>(ex);
            }
        }

        public async Task<Result> ActivateSchemaVersionAsync(
            string eventType,
            int version,
            DateTimeOffset activatedAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string deactivateActiveSql = """
                    UPDATE event_schema_versions
                    SET state_code = @DraftState,
                        activated_at = NULL
                    WHERE event_type = @EventType
                      AND state_code = @ActiveState
                      AND version_number <> @Version;
                    """;

                const string activateTargetSql = """
                    UPDATE event_schema_versions
                    SET state_code = @ActiveState,
                        activated_at = @ActivatedAt,
                        deprecated_at = NULL
                    WHERE event_type = @EventType
                      AND version_number = @Version
                      AND state_code <> @DeprecatedState;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(new CommandDefinition(
                    deactivateActiveSql,
                    new
                    {
                        EventType = eventType,
                        Version = version,
                        DraftState = EventSchemaState.Draft.ToString(),
                        ActiveState = EventSchemaState.Active.ToString()
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                var updated = await connection.ExecuteAsync(new CommandDefinition(
                    activateTargetSql,
                    new
                    {
                        EventType = eventType,
                        Version = version,
                        ActivatedAt = activatedAt,
                        ActiveState = EventSchemaState.Active.ToString(),
                        DeprecatedState = EventSchemaState.Deprecated.ToString()
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                if (updated == 0)
                    return Result.Failure(Error.NotFound(
                        $"Schema version {version} for '{eventType}' was not found."));

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result> DeprecateSchemaVersionAsync(
            string eventType,
            int version,
            DateTimeOffset deprecatedAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    UPDATE event_schema_versions
                    SET state_code = @DeprecatedState,
                        deprecated_at = @DeprecatedAt
                    WHERE event_type = @EventType
                      AND version_number = @Version;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        EventType = eventType,
                        Version = version,
                        DeprecatedAt = deprecatedAt,
                        DeprecatedState = EventSchemaState.Deprecated.ToString()
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                if (rows == 0)
                    return Result.Failure(Error.NotFound(
                        $"Schema version {version} for '{eventType}' was not found."));

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        private NpgsqlConnection CreateConnection() =>
            (NpgsqlConnection)_connectionFactory.CreateConnection();

        private sealed class EventDefinitionRow
        {
            public string event_type { get; set; } = string.Empty;
            public string display_name { get; set; } = string.Empty;
            public string? description { get; set; }
            public DateTimeOffset created_at { get; set; }

            public EventDefinition ToDomain() =>
                EventDefinition.Rehydrate(event_type, display_name, description, created_at);
        }

        private sealed class EventDefinitionRowWithCount
        {
            public string event_type { get; set; } = string.Empty;
            public string display_name { get; set; } = string.Empty;
            public string? description { get; set; }
            public DateTimeOffset created_at { get; set; }
            public long total_count { get; set; }

            public EventDefinition ToDomain() =>
                EventDefinition.Rehydrate(event_type, display_name, description, created_at);
        }

        private sealed class EventSchemaVersionRow
        {
            public string event_type { get; set; } = string.Empty;
            public int version { get; set; }
            public string schema { get; set; } = string.Empty;
            public string compatibility_mode { get; set; } = string.Empty;
            public string state { get; set; } = string.Empty;
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset? activated_at { get; set; }
            public DateTimeOffset? deprecated_at { get; set; }

            public EventSchemaVersion ToDomain() =>
                EventSchemaVersion.Rehydrate(
                    event_type,
                    version,
                    schema,
                    Enum.Parse<SchemaCompatibilityMode>(compatibility_mode, ignoreCase: true),
                    Enum.Parse<EventSchemaState>(state, ignoreCase: true),
                    created_at,
                    activated_at,
                    deprecated_at);
        }

        private sealed class EventSchemaVersionRowWithCount
        {
            public string event_type { get; set; } = string.Empty;
            public int version { get; set; }
            public string schema { get; set; } = string.Empty;
            public string compatibility_mode { get; set; } = string.Empty;
            public string state { get; set; } = string.Empty;
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset? activated_at { get; set; }
            public DateTimeOffset? deprecated_at { get; set; }
            public long total_count { get; set; }

            public EventSchemaVersion ToDomain() =>
                EventSchemaVersion.Rehydrate(
                    event_type,
                    version,
                    schema,
                    Enum.Parse<SchemaCompatibilityMode>(compatibility_mode, ignoreCase: true),
                    Enum.Parse<EventSchemaState>(state, ignoreCase: true),
                    created_at,
                    activated_at,
                    deprecated_at);
        }
    }
}
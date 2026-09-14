using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Events;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
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
    }
}
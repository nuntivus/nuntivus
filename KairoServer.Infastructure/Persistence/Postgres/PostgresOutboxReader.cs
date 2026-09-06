using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Outbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresOutboxReader : IOutboxReader
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresOutboxReader(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Result<IReadOnlyList<OutboxMessage>>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                SELECT
                    id            AS Id,
                    event_id      AS EventId,
                    type          AS Type,
                    payload::text AS Payload,
                    created_at    AS CreatedAt,
                    processed_at  AS ProcessedAt,
                    attempt_count AS AttemptCount
                FROM outbox_messages
                WHERE processed_at IS NULL
                ORDER BY created_at
                LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED;
                """;

                using var connection = _connectionFactory.CreateConnection();

                var rows = await connection.QueryAsync<OutboxRow>(
                    new CommandDefinition(
                        sql,
                        new { BatchSize = batchSize },
                        cancellationToken: cancellationToken));

                return Result.Success<IReadOnlyList<OutboxMessage>>(rows
                    .Select(r => OutboxMessage.Restore(
                        r.Id,
                        r.EventId,
                        r.Type,
                        r.Payload,
                        r.CreatedAt,
                        r.ProcessedAt,
                        r.AttemptCount))
                    .ToList());

            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<OutboxMessage>>(ex);
            }
        }

        public async Task<Result> MarkProcessedAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                UPDATE outbox_messages
                SET processed_at = NOW()
                WHERE id = @Id;
                """;

                using var connection = _connectionFactory.CreateConnection();

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new { Id = id },
                        cancellationToken: cancellationToken));

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result> IncrementAttemptAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                UPDATE outbox_messages
                SET attempt_count = attempt_count + 1
                WHERE id = @Id;
                """;

                using var connection = _connectionFactory.CreateConnection();

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new { Id = id },
                        cancellationToken: cancellationToken));

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        private sealed class OutboxRow
        {
            public required Guid Id { get; set; }
            public required Guid EventId { get; set; }
            public required string Type { get; set; }
            public required string Payload { get; set; }
            public required DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset? ProcessedAt { get; set; }
            public required int AttemptCount { get; set; }
        }
    }
}

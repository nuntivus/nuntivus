using Dapper;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Outbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NuntivusServer.Infastructure.Persistence.Postgres
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
            string workerId,
            TimeSpan claimTtl,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                WITH candidates AS (
                    SELECT id
                    FROM outbox_messages
                    WHERE processed_at IS NULL
                      AND (next_attempt_at IS NULL OR next_attempt_at <= @Now)
                      AND (claim_expires_at IS NULL OR claim_expires_at <= @Now)
                    ORDER BY COALESCE(next_attempt_at, created_at), created_at, id
                    LIMIT @BatchSize
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE outbox_messages o
                SET claimed_by = @WorkerId,
                    claim_expires_at = @ClaimExpiresAt
                FROM candidates c
                WHERE o.id = c.id
                RETURNING
                    o.id            AS Id,
                    o.event_id      AS EventId,
                    o.type          AS Type,
                    o.payload::text AS Payload,
                    o.created_at    AS CreatedAt,
                    o.processed_at  AS ProcessedAt,
                    o.attempt_count AS AttemptCount,
                    o.last_failed_at AS LastFailedAt,
                    o.next_attempt_at AS NextAttemptAt,
                    o.last_error AS LastError;
                """;

                using var connection = _connectionFactory.CreateConnection();

                var now = DateTimeOffset.UtcNow;

                var rows = await connection.QueryAsync<OutboxRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            BatchSize = batchSize,
                            WorkerId = workerId,
                            Now = now,
                            ClaimExpiresAt = now.Add(claimTtl),
                        },
                        cancellationToken: cancellationToken));

                return Result.Success<IReadOnlyList<OutboxMessage>>(rows
                    .Select(r => OutboxMessage.Restore(
                        r.Id,
                        r.EventId,
                        r.Type,
                        r.Payload,
                        r.CreatedAt,
                        r.ProcessedAt,
                        r.AttemptCount,
                        r.LastFailedAt,
                        r.NextAttemptAt,
                        r.LastError))
                    .ToList());

            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<OutboxMessage>>(ex);
            }
        }

        public async Task<Result> MarkProcessedAsync(
            Guid id,
            string workerId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                UPDATE outbox_messages
                SET processed_at = @Now,
                    next_attempt_at = NULL,
                    last_failed_at = NULL,
                    last_error = NULL,
                    claimed_by = NULL,
                    claim_expires_at = NULL
                WHERE id = @Id
                  AND processed_at IS NULL
                  AND claimed_by = @WorkerId;
                """;

                using var connection = _connectionFactory.CreateConnection();

                var rows = await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new { Id = id, WorkerId = workerId, Now = DateTimeOffset.UtcNow },
                        cancellationToken: cancellationToken));

                if (rows == 0)
                    return Result.Failure("outbox.claim", $"Outbox message '{id}' is not claimed by worker '{workerId}'.");

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result> ReleaseClaimAfterFailureAsync(
            Guid id,
            string workerId,
            DateTimeOffset failedAt,
            DateTimeOffset nextAttemptAt,
            string error,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                UPDATE outbox_messages
                SET attempt_count = attempt_count + 1,
                    last_failed_at = @FailedAt,
                    next_attempt_at = @NextAttemptAt,
                    last_error = @Error,
                    claimed_by = NULL,
                    claim_expires_at = NULL
                WHERE id = @Id
                  AND processed_at IS NULL
                  AND claimed_by = @WorkerId;
                """;

                using var connection = _connectionFactory.CreateConnection();

                var rows = await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new { Id = id, WorkerId = workerId, FailedAt = failedAt, NextAttemptAt = nextAttemptAt, Error = error },
                        cancellationToken: cancellationToken));

                if (rows == 0)
                    return Result.Failure("outbox.claim", $"Outbox message '{id}' is not claimed by worker '{workerId}'.");

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result<int>> ReleaseExpiredClaimsAsync(
            string workerId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                UPDATE outbox_messages
                SET claimed_by = NULL,
                    claim_expires_at = NULL
                WHERE processed_at IS NULL
                  AND claimed_by = @WorkerId;
                """;

                using var connection = _connectionFactory.CreateConnection();

                var rows = await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new { WorkerId = workerId },
                        cancellationToken: cancellationToken));

                return Result.Success(rows);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
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
            public DateTimeOffset? LastFailedAt { get; set; }
            public DateTimeOffset? NextAttemptAt { get; set; }
            public string? LastError { get; set; }
        }
    }
}

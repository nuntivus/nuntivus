using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresRetentionCleanupRepository : IRetentionCleanupRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresRetentionCleanupRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<Result<int>> DeleteProcessedOutboxBatchAsync(
            DateTimeOffset processedBefore,
            int batchSize,
            CancellationToken cancellationToken)
        {
            const string sql = """
                WITH candidates AS (
                    SELECT id
                    FROM outbox_messages
                    WHERE processed_at IS NOT NULL
                      AND processed_at < @ProcessedBefore
                      AND claimed_by IS NULL
                    ORDER BY processed_at, id
                    LIMIT @BatchSize
                    FOR UPDATE SKIP LOCKED
                ),
                deleted AS (
                    DELETE FROM outbox_messages o
                    USING candidates c
                    WHERE o.id = c.id
                    RETURNING 1
                )
                SELECT COUNT(*) FROM deleted;
                """;

            return await ExecuteCountAsync(sql, new { ProcessedBefore = processedBefore, BatchSize = batchSize }, cancellationToken);
        }

        public async Task<Result<int>> DeleteDeliveryAttemptsBatchAsync(
            DateTimeOffset completedBefore,
            int batchSize,
            CancellationToken cancellationToken)
        {
            const string sql = """
                WITH candidates AS (
                    SELECT da.id
                    FROM delivery_attempts da
                    JOIN deliveries d ON d.id = da.delivery_id
                    WHERE da.completed_at < @CompletedBefore
                      AND (
                        d.status IN ('Delivered', 'Dead')
                        OR (d.status = 'Failed' AND d.next_attempt_at IS NULL)
                      )
                    ORDER BY da.completed_at
                    LIMIT @BatchSize
                    FOR UPDATE OF da SKIP LOCKED
                ),
                deleted AS (
                    DELETE FROM delivery_attempts da
                    USING candidates c
                    WHERE da.id = c.id
                    RETURNING 1
                )
                SELECT COUNT(*) FROM deleted;
                """;

            return await ExecuteCountAsync(sql, new { CompletedBefore = completedBefore, BatchSize = batchSize }, cancellationToken);
        }

        public async Task<Result<int>> DeleteDeliveriesBatchAsync(
            DateTimeOffset updatedBefore,
            int batchSize,
            CancellationToken cancellationToken)
        {
            const string sql = """
                WITH candidates AS (
                    SELECT id
                    FROM deliveries
                    WHERE updated_at < @UpdatedBefore
                      AND (
                        status IN ('Delivered', 'Dead')
                        OR (status = 'Failed' AND next_attempt_at IS NULL)
                      )
                    ORDER BY updated_at
                    LIMIT @BatchSize
                    FOR UPDATE SKIP LOCKED
                ),
                deleted AS (
                    DELETE FROM deliveries d
                    USING candidates c
                    WHERE d.id = c.id
                    RETURNING 1
                )
                SELECT COUNT(*) FROM deleted;
                """;

            return await ExecuteCountAsync(sql, new { UpdatedBefore = updatedBefore, BatchSize = batchSize }, cancellationToken);
        }

        public async Task<Result<int>> DeleteEventsBatchAsync(
            DateTimeOffset receivedBefore,
            int batchSize,
            CancellationToken cancellationToken)
        {
            const string sql = """
                WITH candidates AS (
                    SELECT e.id
                    FROM events e
                    WHERE e.received_at < @ReceivedBefore
                      AND NOT EXISTS (
                          SELECT 1 FROM deliveries d
                          WHERE d.event_id = e.id
                      )
                      AND NOT EXISTS (
                          SELECT 1 FROM outbox_messages o
                          WHERE o.event_id = e.id
                      )
                    ORDER BY e.received_at
                    LIMIT @BatchSize
                    FOR UPDATE OF e SKIP LOCKED
                ),
                deleted AS (
                    DELETE FROM events e
                    USING candidates c
                    WHERE e.id = c.id
                    RETURNING 1
                )
                SELECT COUNT(*) FROM deleted;
                """;

            return await ExecuteCountAsync(sql, new { ReceivedBefore = receivedBefore, BatchSize = batchSize }, cancellationToken);
        }

        private async Task<Result<int>> ExecuteCountAsync(string sql, object param, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var count = await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(sql, param, cancellationToken: cancellationToken));

                return Result.Success(count);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
            }
        }
    }
}

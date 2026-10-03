using Dapper;
using Npgsql;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresSourceIngestionStateRepository : ISourceIngestionStateRepository
    {
        private readonly IDatabaseConnectionFactory _connections;

        public PostgresSourceIngestionStateRepository(IDatabaseConnectionFactory connections) => _connections = connections;

        public async Task<Result<SourceLease?>> TryAcquireAsync(string sourceId, string ownerId, TimeSpan duration, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connections.CreateConnection();
                var lease = await connection.QuerySingleOrDefaultAsync<LeaseRow>(new CommandDefinition(
                    """
                    INSERT INTO source_ingestion_leases(source_id, owner_id, epoch, expires_at)
                    SELECT @SourceId, @OwnerId, 1, now() + @Duration FROM sources WHERE id = @SourceId AND enabled
                    ON CONFLICT (source_id) DO UPDATE SET owner_id = @OwnerId, epoch = source_ingestion_leases.epoch + 1,
                        expires_at = now() + @Duration
                    WHERE source_ingestion_leases.expires_at <= now()
                      AND EXISTS (SELECT 1 FROM sources WHERE id = @SourceId AND enabled)
                    RETURNING source_id AS SourceId, owner_id AS OwnerId, epoch AS Epoch
                    """, new { SourceId = Guid.Parse(sourceId), OwnerId = ownerId, Duration = duration }, cancellationToken: cancellationToken));
                return Result.Success<SourceLease?>(lease is null ? null : new SourceLease(lease.SourceId.ToString("D"), lease.OwnerId, lease.Epoch));
            }
            catch (Exception ex) { return Result.Failure<SourceLease?>(ex); }
        }

        public async Task<Result<bool>> RenewAsync(SourceLease lease, TimeSpan duration, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connections.CreateConnection();
                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE source_ingestion_leases SET expires_at = now() + @Duration
                    WHERE source_id = @SourceId AND owner_id = @OwnerId AND epoch = @Epoch AND expires_at > now()
                      AND EXISTS (SELECT 1 FROM sources WHERE id = @SourceId AND enabled)
                    """, Args(lease, duration), cancellationToken: cancellationToken));
                return Result.Success(rows == 1);
            }
            catch (Exception ex) { return Result.Failure<bool>(ex); }
        }

        public async Task<Result> ReleaseAsync(SourceLease lease, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connections.CreateConnection();
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE source_ingestion_leases SET expires_at = now() WHERE source_id = @SourceId AND owner_id = @OwnerId AND epoch = @Epoch",
                    Args(lease), cancellationToken: cancellationToken));
                return Result.Success();
            }
            catch (Exception ex) { return Result.Failure(ex); }
        }

        public async Task<Result<bool>> HasProcessedAsync(string sourceId, string itemKey, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connections.CreateConnection();
                return Result.Success(await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS(SELECT 1 FROM source_ingestion_progress WHERE source_id = @SourceId AND item_key = @ItemKey)",
                    new { SourceId = Guid.Parse(sourceId), ItemKey = itemKey }, cancellationToken: cancellationToken)));
            }
            catch (Exception ex) { return Result.Failure<bool>(ex); }
        }

        public async Task<Result<bool>> MarkProcessedAsync(SourceLease lease, string itemKey, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connections.CreateConnection();
                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO source_ingestion_progress(source_id, item_key)
                    SELECT @SourceId, @ItemKey FROM source_ingestion_leases
                    WHERE source_id = @SourceId AND owner_id = @OwnerId AND epoch = @Epoch AND expires_at > now()
                      AND EXISTS (SELECT 1 FROM sources WHERE id = @SourceId AND enabled)
                    ON CONFLICT (source_id, item_key) DO UPDATE SET processed_at = source_ingestion_progress.processed_at
                    """, new { SourceId = Guid.Parse(lease.SourceId), lease.OwnerId, lease.Epoch, ItemKey = itemKey }, cancellationToken: cancellationToken));
                return Result.Success(rows == 1);
            }
            catch (Exception ex) { return Result.Failure<bool>(ex); }
        }

        private static object Args(SourceLease lease, TimeSpan? duration = null) => new
        {
            SourceId = Guid.Parse(lease.SourceId), lease.OwnerId, lease.Epoch, Duration = duration
        };

        private sealed class LeaseRow
        {
            public Guid SourceId { get; set; }
            public string OwnerId { get; set; } = string.Empty;
            public long Epoch { get; set; }
        }
    }
}

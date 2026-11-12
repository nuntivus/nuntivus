using Dapper;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresSloObservationRetention(IDatabaseConnectionFactory connections) : ISloObservationRetention
    {
        public async Task<Result<int>> DeleteExpiredBatchAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var connection = connections.CreateConnection();
                var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                    WITH candidates AS (
                        SELECT delivery_id FROM delivery_slo_observations
                        WHERE created_at < now() - interval '45 days'
                        ORDER BY created_at LIMIT 5000 FOR UPDATE SKIP LOCKED
                    ), deleted AS (
                        DELETE FROM delivery_slo_observations WHERE delivery_id IN (SELECT delivery_id FROM candidates) RETURNING 1
                    ) SELECT count(*)::int FROM deleted;
                    """, cancellationToken: cancellationToken));
                return Result.Success(count);
            }
            catch (Exception ex) { return Result.Failure<int>(ex); }
        }
    }
}

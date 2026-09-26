using System.Data.Common;
using NuntivusServer.Application.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class PostgresConnectionHealthCheck : IHealthCheck
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresConnectionHealthCheck(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (_connectionFactory.CreateConnection() is not DbConnection connection)
            {
                return HealthCheckResult.Unhealthy("Database connection factory did not return a DbConnection instance.");
            }

            await using (connection)
            {
                await connection.OpenAsync(cancellationToken);

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(cancellationToken);
            }

            return HealthCheckResult.Healthy();
        }
    }
}

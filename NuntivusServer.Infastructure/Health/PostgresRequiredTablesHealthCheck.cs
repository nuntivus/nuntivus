using NuntivusServer.Application.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class PostgresRequiredTablesHealthCheck : IHealthCheck
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;
        private readonly RequiredTablesReadinessOption _option;

        public PostgresRequiredTablesHealthCheck(
            IDatabaseConnectionFactory connectionFactory,
            RequiredTablesReadinessOption option)
        {
            _connectionFactory = connectionFactory;
            _option = option;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (_option.Tables.Length == 0)
            {
                return HealthCheckResult.Unhealthy("No required tables configured for migration readiness.");
            }

            await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(@"
SELECT unnest(@required_tables::text[])
EXCEPT
SELECT table_name
FROM information_schema.tables
WHERE table_schema = @schema;", connection);

            command.Parameters.AddWithValue("required_tables", _option.Tables);
            command.Parameters.AddWithValue("schema", _option.Schema);

            var missingTables = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                missingTables.Add(reader.GetString(0));
            }

            if (missingTables.Count > 0)
            {
                return HealthCheckResult.Unhealthy($"Missing required tables in schema '{_option.Schema}': {string.Join(", ", missingTables)}");
            }

            return HealthCheckResult.Healthy();
        }
    }
}

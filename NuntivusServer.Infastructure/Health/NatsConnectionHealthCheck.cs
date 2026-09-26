using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.Core;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class NatsConnectionHealthCheck : IHealthCheck
    {
        private readonly INatsConnection _connection;

        public NatsConnectionHealthCheck(INatsConnection connection)
        {
            _connection = connection;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            await _connection.ConnectAsync();
            return HealthCheckResult.Healthy();
        }
    }
}

using KairoServer.Infastructure.Messaging.Nats;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.JetStream;

namespace KairoServer.Infastructure.Health
{
    public sealed class JetStreamHealthCheck : IHealthCheck
    {
        private readonly INatsJSContext _jetStream;
        private readonly NatsOption _option;

        public JetStreamHealthCheck(INatsJSContext jetStream, NatsOption option)
        {
            _jetStream = jetStream;
            _option = option;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            await _jetStream.GetStreamAsync(_option.Stream, cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
    }
}

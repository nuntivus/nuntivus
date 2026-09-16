using System;
using System.Threading;
using System.Threading.Tasks;

namespace KairoServer.Application.Deliveries.Execution
{
    public interface IDeliveryDispatcherMetrics
    {
        ValueTask TrackDispatchOutcomeAsync(string outcome, CancellationToken cancellationToken);

        ValueTask TrackRateLimitDecisionAsync(string decision, CancellationToken cancellationToken);

        ValueTask TrackRateLimitDelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }
}
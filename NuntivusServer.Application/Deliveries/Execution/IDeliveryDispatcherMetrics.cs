using System;
using System.Threading;
using System.Threading.Tasks;

namespace NuntivusServer.Application.Deliveries.Execution
{
    public interface IDeliveryDispatcherMetrics
    {
        ValueTask TrackDispatchOutcomeAsync(
            string outcome,
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken);

        ValueTask TrackRateLimitDecisionAsync(string decision, CancellationToken cancellationToken);

        ValueTask TrackRateLimitDelayAsync(TimeSpan delay, CancellationToken cancellationToken);

        ValueTask TrackDispatchLatencyAsync(
            TimeSpan duration,
            string outcome,
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken);

        ValueTask TrackDeliveryLatencyAsync(
            TimeSpan duration,
            string outcome,
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken);

        ValueTask TrackDestinationLatencyAsync(
            string destinationType,
            TimeSpan duration,
            string outcome,
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken);

        ValueTask TrackRetryCountAsync(
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken);

        ValueTask TrackDeadDeliveryCountAsync(
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken);

        ValueTask TrackDestinationHealthStatusAsync(
            string status,
            string customerId,
            Guid contractId,
            CancellationToken cancellationToken);
    }
}
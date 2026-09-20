using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Observability;

namespace KairoServer.Application.Deliveries.Execution
{
    public sealed class MeterDeliveryDispatcherMetrics : IDeliveryDispatcherMetrics
    {
        private static readonly Counter<long> DispatchOutcomeCounter = KairoTelemetry.Meter.CreateCounter<long>("kairo.delivery.dispatch.outcome");
        private static readonly Counter<long> DeliverySuccessCounter = KairoTelemetry.Meter.CreateCounter<long>("kairo.delivery.success.count");
        private static readonly Counter<long> DeliveryFailureCounter = KairoTelemetry.Meter.CreateCounter<long>("kairo.delivery.failure.count");
        private static readonly Counter<long> RetryCounter = KairoTelemetry.Meter.CreateCounter<long>("kairo.delivery.retry.count");
        private static readonly Counter<long> DeadDeliveryCounter = KairoTelemetry.Meter.CreateCounter<long>("kairo.delivery.dead.count");
        private static readonly Counter<long> RateLimitDecisionCounter = KairoTelemetry.Meter.CreateCounter<long>("kairo.delivery.rate_limit.decision");
        private static readonly Histogram<double> RateLimitDelayMs = KairoTelemetry.Meter.CreateHistogram<double>("kairo.delivery.rate_limit.delay.ms");
        private static readonly Histogram<double> DispatchLatencyMs = KairoTelemetry.Meter.CreateHistogram<double>("kairo.delivery.dispatch.latency.ms");
        private static readonly Histogram<double> DeliveryLatencyMs = KairoTelemetry.Meter.CreateHistogram<double>("kairo.delivery.latency.ms");
        private static readonly Histogram<double> DestinationLatencyMs = KairoTelemetry.Meter.CreateHistogram<double>("kairo.delivery.destination.latency.ms");
        private static readonly Counter<long> DestinationHealthStatusCounter = KairoTelemetry.Meter.CreateCounter<long>("kairo.delivery.destination.health.status");

        public ValueTask TrackDispatchOutcomeAsync(
            string outcome,
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken)
        {
            var tags = CreateCorrelationTags(outcome, eventId, deliveryId, contractId);
            DispatchOutcomeCounter.Add(1, tags);

            if (outcome is "delivered")
            {
                DeliverySuccessCounter.Add(1, tags);
            }
            else if (outcome is "failed_retry" or "failed_terminal")
            {
                DeliveryFailureCounter.Add(1, tags);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask TrackRateLimitDecisionAsync(string decision, CancellationToken cancellationToken)
        {
            RateLimitDecisionCounter.Add(1, new KeyValuePair<string, object?>("decision", decision));
            return ValueTask.CompletedTask;
        }

        public ValueTask TrackRateLimitDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.Zero)
                RateLimitDelayMs.Record(delay.TotalMilliseconds);

            return ValueTask.CompletedTask;
        }

        public ValueTask TrackDispatchLatencyAsync(
            TimeSpan duration,
            string outcome,
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken)
        {
            if (duration > TimeSpan.Zero)
            {
                DispatchLatencyMs.Record(duration.TotalMilliseconds, CreateCorrelationTags(outcome, eventId, deliveryId, contractId));
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask TrackDeliveryLatencyAsync(
            TimeSpan duration,
            string outcome,
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken)
        {
            if (duration > TimeSpan.Zero)
            {
                DeliveryLatencyMs.Record(duration.TotalMilliseconds, CreateCorrelationTags(outcome, eventId, deliveryId, contractId));
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask TrackDestinationLatencyAsync(
            string destinationType,
            TimeSpan duration,
            string outcome,
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken)
        {
            if (duration <= TimeSpan.Zero)
                return ValueTask.CompletedTask;

            var tags = CreateCorrelationTags(outcome, eventId, deliveryId, contractId);
            tags.Add("destinationType", destinationType);
            DestinationLatencyMs.Record(duration.TotalMilliseconds, tags);
            return ValueTask.CompletedTask;
        }

        public ValueTask TrackRetryCountAsync(
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken)
        {
            RetryCounter.Add(1, CreateCorrelationTags("retry", eventId, deliveryId, contractId));
            return ValueTask.CompletedTask;
        }

        public ValueTask TrackDeadDeliveryCountAsync(
            Guid eventId,
            Guid deliveryId,
            Guid contractId,
            CancellationToken cancellationToken)
        {
            DeadDeliveryCounter.Add(1, CreateCorrelationTags("dead", eventId, deliveryId, contractId));
            return ValueTask.CompletedTask;
        }

        public ValueTask TrackDestinationHealthStatusAsync(
            string status,
            string customerId,
            Guid contractId,
            CancellationToken cancellationToken)
        {
            var tags = new TagList
            {
                { "status", status },
                { KairoTelemetry.CustomerIdTag, customerId },
                { KairoTelemetry.ContractIdTag, contractId.ToString("D") },
            };

            DestinationHealthStatusCounter.Add(1, tags);
            return ValueTask.CompletedTask;
        }

        private static TagList CreateCorrelationTags(string outcome, Guid eventId, Guid deliveryId, Guid contractId)
        {
            return new TagList
            {
                { "outcome", outcome },
                { KairoTelemetry.EventIdTag, eventId.ToString("D") },
                { KairoTelemetry.DeliveryIdTag, deliveryId.ToString("D") },
                { KairoTelemetry.ContractIdTag, contractId.ToString("D") },
            };
        }
    }
}
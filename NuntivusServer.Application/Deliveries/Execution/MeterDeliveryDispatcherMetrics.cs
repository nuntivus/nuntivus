using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Observability;

namespace NuntivusServer.Application.Deliveries.Execution
{
    public sealed class MeterDeliveryDispatcherMetrics : IDeliveryDispatcherMetrics
    {
        private static readonly Counter<long> DispatchOutcomeCounter = NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.delivery.dispatch.outcome");
        private static readonly Counter<long> DeliverySuccessCounter = NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.delivery.success.count");
        private static readonly Counter<long> DeliveryFailureCounter = NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.delivery.failure.count");
        private static readonly Counter<long> RetryCounter = NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.delivery.retry.count");
        private static readonly Counter<long> DeadDeliveryCounter = NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.delivery.dead.count");
        private static readonly Counter<long> RateLimitDecisionCounter = NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.delivery.rate_limit.decision");
        private static readonly Histogram<double> RateLimitDelayMs = NuntivusTelemetry.Meter.CreateHistogram<double>("nuntivus.delivery.rate_limit.delay.ms");
        private static readonly Histogram<double> DispatchLatencyMs = NuntivusTelemetry.Meter.CreateHistogram<double>("nuntivus.delivery.dispatch.latency.ms");
        private static readonly Histogram<double> DeliveryLatencyMs = NuntivusTelemetry.Meter.CreateHistogram<double>("nuntivus.delivery.latency.ms");
        private static readonly Histogram<double> DestinationLatencyMs = NuntivusTelemetry.Meter.CreateHistogram<double>("nuntivus.delivery.destination.latency.ms");
        private static readonly Counter<long> DestinationHealthStatusCounter = NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.delivery.destination.health.status");

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
                { NuntivusTelemetry.CustomerIdTag, customerId },
                { NuntivusTelemetry.ContractIdTag, contractId.ToString("D") },
            };

            DestinationHealthStatusCounter.Add(1, tags);
            return ValueTask.CompletedTask;
        }

        private static TagList CreateCorrelationTags(string outcome, Guid eventId, Guid deliveryId, Guid contractId)
        {
            return new TagList
            {
                { "outcome", outcome },
                { NuntivusTelemetry.EventIdTag, eventId.ToString("D") },
                { NuntivusTelemetry.DeliveryIdTag, deliveryId.ToString("D") },
                { NuntivusTelemetry.ContractIdTag, contractId.ToString("D") },
            };
        }
    }
}
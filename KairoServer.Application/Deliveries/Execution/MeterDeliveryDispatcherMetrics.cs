using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

namespace KairoServer.Application.Deliveries.Execution
{
    public sealed class MeterDeliveryDispatcherMetrics : IDeliveryDispatcherMetrics
    {
        private static readonly Meter Meter = new("KairoServer.Deliveries.Dispatcher");
        private static readonly Counter<long> DispatchOutcomeCounter = Meter.CreateCounter<long>("kairo.delivery.dispatch.outcome");
        private static readonly Counter<long> RateLimitDecisionCounter = Meter.CreateCounter<long>("kairo.delivery.rate_limit.decision");
        private static readonly Histogram<double> RateLimitDelayMs = Meter.CreateHistogram<double>("kairo.delivery.rate_limit.delay.ms");

        public ValueTask TrackDispatchOutcomeAsync(string outcome, CancellationToken cancellationToken)
        {
            DispatchOutcomeCounter.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
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
    }
}
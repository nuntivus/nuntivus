using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using KairoServer.Application.Observability;

namespace KairoServer.Worker.Observability
{
    internal static class WorkerMetrics
    {
        private static readonly Counter<long> IngestionThroughputCounter =
            KairoTelemetry.Meter.CreateCounter<long>("kairo.ingestion.throughput.count");

        private static readonly Histogram<double> OutboxLagMs =
            KairoTelemetry.Meter.CreateHistogram<double>("kairo.outbox.lag.ms");

        private static readonly Counter<long> OutboxClaimedCounter =
            KairoTelemetry.Meter.CreateCounter<long>("kairo.outbox.claimed.count");

        private static readonly Counter<long> OutboxOutcomeCounter =
            KairoTelemetry.Meter.CreateCounter<long>("kairo.outbox.outcome.count");

        private static readonly Histogram<double> OutboxRetryDelayMs =
            KairoTelemetry.Meter.CreateHistogram<double>("kairo.outbox.retry.delay.ms");

        private static readonly Histogram<long> OutboxAttemptCount =
            KairoTelemetry.Meter.CreateHistogram<long>("kairo.outbox.attempt.count");

        private static readonly Histogram<double> JetStreamConsumerLagMs =
            KairoTelemetry.Meter.CreateHistogram<double>("kairo.jetstream.consumer.lag.ms");

        private static readonly Histogram<long> JetStreamConsumerPendingCount =
            KairoTelemetry.Meter.CreateHistogram<long>("kairo.jetstream.consumer.pending.count");

        private static readonly Histogram<long> JetStreamConsumerDeliveryCount =
            KairoTelemetry.Meter.CreateHistogram<long>("kairo.jetstream.consumer.delivery.count");

        private static readonly Histogram<double> MatcherLatencyMs =
            KairoTelemetry.Meter.CreateHistogram<double>("kairo.matcher.latency.ms");

        private static readonly Counter<long> WorkerProcessedCounter =
            KairoTelemetry.Meter.CreateCounter<long>("kairo.worker.processed.count");

        private static readonly Counter<long> DeliverySchedulerClaimedCounter =
            KairoTelemetry.Meter.CreateCounter<long>("kairo.delivery.scheduler.claimed.count");

        private static readonly Counter<long> DeliverySchedulerOutcomeCounter =
            KairoTelemetry.Meter.CreateCounter<long>("kairo.delivery.scheduler.outcome.count");

        public static void TrackIngestionThroughput(string subject)
        {
            IngestionThroughputCounter.Add(1, new TagList
            {
                { "subject", subject },
            });
        }

        public static void TrackOutboxLag(TimeSpan lag, string messageType)
        {
            if (lag <= TimeSpan.Zero)
                return;

            OutboxLagMs.Record(lag.TotalMilliseconds, new TagList
            {
                { "messageType", messageType },
            });
        }

        public static void TrackOutboxClaimed(int count, string worker)
        {
            if (count <= 0)
                return;

            OutboxClaimedCounter.Add(count, new TagList
            {
                { KairoTelemetry.WorkerTag, worker },
            });
        }

        public static void TrackOutboxOutcome(string outcome, string messageType)
        {
            OutboxOutcomeCounter.Add(1, new TagList
            {
                { "outcome", outcome },
                { "messageType", messageType },
            });
        }

        public static void TrackOutboxRetry(TimeSpan delay, int attempt, string messageType)
        {
            if (delay >= TimeSpan.Zero)
            {
                OutboxRetryDelayMs.Record(delay.TotalMilliseconds, new TagList
                {
                    { "messageType", messageType },
                });
            }

            if (attempt > 0)
            {
                OutboxAttemptCount.Record(attempt, new TagList
                {
                    { "messageType", messageType },
                });
            }
        }

        public static void TrackJetStreamConsumerLag(TimeSpan lag, string worker)
        {
            if (lag <= TimeSpan.Zero)
                return;

            JetStreamConsumerLagMs.Record(lag.TotalMilliseconds, new TagList
            {
                { KairoTelemetry.WorkerTag, worker },
            });
        }

        public static void TrackJetStreamConsumerPending(long pending, string worker)
        {
            if (pending < 0)
                return;

            JetStreamConsumerPendingCount.Record(pending, new TagList
            {
                { KairoTelemetry.WorkerTag, worker },
            });
        }

        public static void TrackJetStreamConsumerDeliveryCount(long deliveries, string worker)
        {
            if (deliveries <= 0)
                return;

            JetStreamConsumerDeliveryCount.Record(deliveries, new TagList
            {
                { KairoTelemetry.WorkerTag, worker },
            });
        }

        public static void TrackMatcherLatency(TimeSpan latency, string outcome)
        {
            if (latency <= TimeSpan.Zero)
                return;

            MatcherLatencyMs.Record(latency.TotalMilliseconds, new TagList
            {
                { "outcome", outcome },
            });
        }

        public static void TrackWorkerProcessed(string worker, string outcome)
        {
            WorkerProcessedCounter.Add(1, new TagList
            {
                { KairoTelemetry.WorkerTag, worker },
                { "outcome", outcome },
            });
        }

        public static void TrackDeliverySchedulerClaimed(int count)
        {
            if (count <= 0)
                return;

            DeliverySchedulerClaimedCounter.Add(count);
        }

        public static void TrackDeliverySchedulerOutcome(string outcome)
        {
            DeliverySchedulerOutcomeCounter.Add(1, new TagList
            {
                { "outcome", outcome },
            });
        }
    }
}

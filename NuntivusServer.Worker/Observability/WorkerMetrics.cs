using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using NuntivusServer.Application.Observability;

namespace NuntivusServer.Worker.Observability
{
    internal static class WorkerMetrics
    {
        private static readonly Counter<long> IngestionThroughputCounter =
            NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.ingestion.throughput.count");

        private static readonly Histogram<double> OutboxLagMs =
            NuntivusTelemetry.Meter.CreateHistogram<double>("nuntivus.outbox.lag.ms");

        private static readonly Counter<long> OutboxClaimedCounter =
            NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.outbox.claimed.count");

        private static readonly Counter<long> OutboxOutcomeCounter =
            NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.outbox.outcome.count");

        private static readonly Histogram<double> OutboxRetryDelayMs =
            NuntivusTelemetry.Meter.CreateHistogram<double>("nuntivus.outbox.retry.delay.ms");

        private static readonly Histogram<long> OutboxAttemptCount =
            NuntivusTelemetry.Meter.CreateHistogram<long>("nuntivus.outbox.attempt.count");

        private static readonly Histogram<double> JetStreamConsumerLagMs =
            NuntivusTelemetry.Meter.CreateHistogram<double>("nuntivus.jetstream.consumer.lag.ms");

        private static readonly Histogram<long> JetStreamConsumerPendingCount =
            NuntivusTelemetry.Meter.CreateHistogram<long>("nuntivus.jetstream.consumer.pending.count");

        private static readonly Histogram<long> JetStreamConsumerDeliveryCount =
            NuntivusTelemetry.Meter.CreateHistogram<long>("nuntivus.jetstream.consumer.delivery.count");

        private static readonly Histogram<double> MatcherLatencyMs =
            NuntivusTelemetry.Meter.CreateHistogram<double>("nuntivus.matcher.latency.ms");

        private static readonly Counter<long> WorkerProcessedCounter =
            NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.worker.processed.count");

        private static readonly Counter<long> DeliverySchedulerClaimedCounter =
            NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.delivery.scheduler.claimed.count");

        private static readonly Counter<long> DeliverySchedulerOutcomeCounter =
            NuntivusTelemetry.Meter.CreateCounter<long>("nuntivus.delivery.scheduler.outcome.count");

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
                { NuntivusTelemetry.WorkerTag, worker },
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
                { NuntivusTelemetry.WorkerTag, worker },
            });
        }

        public static void TrackJetStreamConsumerPending(long pending, string worker)
        {
            if (pending < 0)
                return;

            JetStreamConsumerPendingCount.Record(pending, new TagList
            {
                { NuntivusTelemetry.WorkerTag, worker },
            });
        }

        public static void TrackJetStreamConsumerDeliveryCount(long deliveries, string worker)
        {
            if (deliveries <= 0)
                return;

            JetStreamConsumerDeliveryCount.Record(deliveries, new TagList
            {
                { NuntivusTelemetry.WorkerTag, worker },
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
                { NuntivusTelemetry.WorkerTag, worker },
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

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

        private static readonly Histogram<double> JetStreamConsumerLagMs =
            KairoTelemetry.Meter.CreateHistogram<double>("kairo.jetstream.consumer.lag.ms");

        private static readonly Histogram<double> MatcherLatencyMs =
            KairoTelemetry.Meter.CreateHistogram<double>("kairo.matcher.latency.ms");

        private static readonly Counter<long> WorkerProcessedCounter =
            KairoTelemetry.Meter.CreateCounter<long>("kairo.worker.processed.count");

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

        public static void TrackJetStreamConsumerLag(TimeSpan lag, string worker)
        {
            if (lag <= TimeSpan.Zero)
                return;

            JetStreamConsumerLagMs.Record(lag.TotalMilliseconds, new TagList
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
    }
}

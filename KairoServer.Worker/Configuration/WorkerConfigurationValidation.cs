using KairoServer.Infastructure.Messaging.Nats;
using KairoServer.Worker.Deliveries;
using KairoServer.Worker.Matching;
using KairoServer.Worker.Outbox;

namespace KairoServer.Worker.Configuration
{
    public static class WorkerConfigurationValidation
    {
        public static bool IsRuntimeValid(WorkerRuntimeOption option)
            => option.EnableOutboxPublisher
               || option.EnableContractMatcher
               || option.EnableDeliveryDispatcher
               || option.EnableDeliveryRecovery
               || option.EnableRetentionCleanup;

        public static bool IsDeliveryConsumerValid(DeliveryOption option)
            => !string.IsNullOrWhiteSpace(option.DurableName)
               && !string.IsNullOrWhiteSpace(option.FilterSubject)
               && option.BatchSize > 0
               && option.MaxConcurrentMessages > 0
               && option.FetchTimeout > TimeSpan.Zero
               && option.NakDelay >= TimeSpan.Zero
               && option.MaxAckPending > 0
               && option.AckWait > TimeSpan.Zero
               && option.MaxDeliver > 0;

        public static bool IsMatcherConsumerValid(MatcherOption option)
            => !string.IsNullOrWhiteSpace(option.DurableName)
               && !string.IsNullOrWhiteSpace(option.FilterSubject)
               && option.BatchSize > 0
               && option.MaxConcurrentMessages > 0
               && option.FetchTimeout > TimeSpan.Zero
               && option.NakDelay >= TimeSpan.Zero
               && option.MaxAckPending > 0
               && option.AckWait > TimeSpan.Zero
               && option.MaxDeliver > 0;

        public static bool IsMatcherConsumerTopologyValid(
            MatcherOption option,
            NatsOption nats,
            IDurableConsumerNamer namer)
            => IsConsumerTopologyValid(option.WorkerName, option.DurableName, option.FilterSubject, nats, namer);

        public static bool IsDeliveryConsumerTopologyValid(
            DeliveryOption option,
            NatsOption nats,
            IDurableConsumerNamer namer)
            => IsConsumerTopologyValid(option.WorkerName, option.DurableName, option.FilterSubject, nats, namer);

        public static bool IsOutboxValid(OutboxOption option)
            => option.BatchSize > 0
               && option.PollDelay > TimeSpan.Zero
               && option.EmptyPollDelay > TimeSpan.Zero
               && option.MaxConcurrentPublishes > 0
               && option.ClaimWorkerCount > 0
               && option.ClaimTtl > TimeSpan.Zero
               && option.FailureBackoffBaseDelay > TimeSpan.Zero
               && option.FailureBackoffMaxDelay >= option.FailureBackoffBaseDelay
               && option.FailureBackoffJitterRatio is >= 0 and <= 1;

        public static bool IsDeliveryRecoveryValid(DeliveryRecoveryOption option)
            => option.RunInterval > TimeSpan.Zero
               && option.StaleInFlightAfter > TimeSpan.Zero
               && option.BatchSize > 0;

        private static bool IsConsumerTopologyValid(
            string workerName,
            string? durableName,
            string filterSubject,
            NatsOption nats,
            IDurableConsumerNamer namer)
        {
            if (string.IsNullOrWhiteSpace(filterSubject)
                || nats.Subjects is null
                || nats.Subjects.Length == 0)
            {
                return false;
            }

            var resolvedDurable = namer.GetDurableName(workerName, durableName);
            if (resolvedDurable.Length > 64)
                return false;

            foreach (var streamSubject in nats.Subjects)
            {
                if (SubjectMatchesFilter(streamSubject, filterSubject))
                    return true;
            }

            return false;
        }

        private static bool SubjectMatchesFilter(string streamSubject, string filterSubject)
        {
            if (string.Equals(streamSubject, filterSubject, StringComparison.Ordinal))
                return true;

            if (streamSubject.EndsWith(">", StringComparison.Ordinal))
            {
                var prefix = streamSubject[..^1];
                return filterSubject.StartsWith(prefix, StringComparison.Ordinal);
            }

            return false;
        }
    }
}
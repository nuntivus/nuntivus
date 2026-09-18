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
               && option.AckWait > TimeSpan.Zero;

        public static bool IsMatcherConsumerValid(MatcherOption option)
            => !string.IsNullOrWhiteSpace(option.DurableName)
               && !string.IsNullOrWhiteSpace(option.FilterSubject)
               && option.BatchSize > 0
               && option.MaxConcurrentMessages > 0
               && option.FetchTimeout > TimeSpan.Zero
               && option.NakDelay >= TimeSpan.Zero
               && option.MaxAckPending > 0;

        public static bool IsOutboxValid(OutboxOption option)
            => option.BatchSize > 0
               && option.PollDelay > TimeSpan.Zero
               && option.MaxConcurrentPublishes > 0
               && option.ClaimTtl > TimeSpan.Zero;

        public static bool IsDeliveryRecoveryValid(DeliveryRecoveryOption option)
            => option.RunInterval > TimeSpan.Zero
               && option.StaleInFlightAfter > TimeSpan.Zero
               && option.BatchSize > 0;
    }
}
using KairoServer.Worker.Deliveries;
using KairoServer.Worker.Matching;

namespace KairoServer.Worker.Configuration
{
    public static class WorkerConfigurationValidation
    {
        public static bool IsRuntimeValid(WorkerRuntimeOption option)
            => option.EnableOutboxPublisher
               || option.EnableContractMatcher
               || option.EnableDeliveryDispatcher
               || option.EnableRetentionCleanup;

        public static bool IsDeliveryConsumerValid(DeliveryOption option)
            => !string.IsNullOrWhiteSpace(option.DurableName)
               && !string.IsNullOrWhiteSpace(option.FilterSubject)
               && option.BatchSize > 0
               && option.FetchTimeout > TimeSpan.Zero
               && option.NakDelay >= TimeSpan.Zero
               && option.MaxAckPending > 0
               && option.AckWait > TimeSpan.Zero;

        public static bool IsMatcherConsumerValid(MatcherOption option)
            => !string.IsNullOrWhiteSpace(option.DurableName)
               && !string.IsNullOrWhiteSpace(option.FilterSubject)
               && option.BatchSize > 0
               && option.FetchTimeout > TimeSpan.Zero
               && option.NakDelay >= TimeSpan.Zero
               && option.MaxAckPending > 0;
    }
}
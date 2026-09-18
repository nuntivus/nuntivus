namespace KairoServer.Worker.Configuration
{
    public sealed class WorkerRuntimeOption
    {
        public bool EnableOutboxPublisher { get; set; } = true;

        public bool EnableContractMatcher { get; set; } = true;

        public bool EnableDeliveryDispatcher { get; set; } = true;

        public bool EnableDeliveryRecovery { get; set; } = true;

        public bool EnableRetentionCleanup { get; set; } = true;
    }
}
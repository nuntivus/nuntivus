namespace KairoServer.Infastructure.Configuration
{
    public static class KairoConfigurationSections
    {
        public const string Database = "Database";
        public const string HostOptions = "HostOptions";
        public const string ApiSecurity = "ApiSecurity";
        public const string Cache = "Cache";
        public const string DestinationCredentials = "DestinationCredentials";
        public const string EndpointSecurity = "EndpointSecurity";
        public const string LegacyDestinationSecurity = "DestinationSecurity";
        public const string Delivery = "Delivery";
        public const string DeliveryScheduler = "DeliveryScheduler";
        public const string DeliveryRecovery = "DeliveryRecovery";
        public const string DeliveryDispatcher = "DeliveryDispatcher";
        public const string DeliveryOrdering = "DeliveryOrdering";
        public const string Outbox = "Outbox";
        public const string RetentionCleanup = "RetentionCleanup";
        public const string Nats = "Nats";
        public const string Matcher = "Matcher";
    }
}
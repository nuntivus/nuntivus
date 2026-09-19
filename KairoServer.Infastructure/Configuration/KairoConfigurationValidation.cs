using KairoServer.Application.Caching;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Retention;
using KairoServer.Infastructure.Deliveries.Http;
using KairoServer.Infastructure.Messaging.Nats;

namespace KairoServer.Infastructure.Configuration
{
    public static class KairoConfigurationValidation
    {
        public static bool HasPostgresConnection(DatabaseOption option)
            => !string.IsNullOrWhiteSpace(option.PostgresConnectionString);

        public static bool IsCacheTtlValid(CacheOption option)
            => option.Ttl > TimeSpan.Zero;

        public static bool IsDestinationCredentialKeyValid(DestinationCredentialOption option)
        {
            if (string.IsNullOrWhiteSpace(option.EncryptionKey))
            {
                return false;
            }

            try
            {
                var keyBytes = Convert.FromBase64String(option.EncryptionKey);
                return keyBytes.Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static HttpDestinationSecurityOption ApplySafeEndpointDefaults(
            HttpDestinationSecurityOption option,
            bool isProduction)
        {
            if (isProduction)
            {
                option.AllowHttpScheme = false;
                option.AllowPrivateNetworkDestinations = false;
                option.AllowRedirects = false;
            }

            return option;
        }

        public static bool IsEndpointSecurityValid(HttpDestinationSecurityOption option)
            => option.MaxResponseBodyBytes > 0
               && option.MaxHeaders > 0
               && option.MaxConfigurationBytes > 0;

        public static bool IsRetryPolicyValid(DeliveryDispatcherOption option)
            => option.MaxAttempts > 0
               && option.BaseBackoff > TimeSpan.Zero
               && option.MaxBackoff >= option.BaseBackoff
               && option.JitterRatio >= 0
               && option.JitterRatio <= 1;

        public static bool IsOrderingValid(DeliveryOrderingOption option)
            => option.LeaseDuration > TimeSpan.Zero;

        public static bool IsRetentionValid(RetentionCleanupOption option)
            => option.RunInterval > TimeSpan.Zero
               && option.BatchSize > 0
               && option.MaxBatchesPerCycle > 0
               && option.InterBatchDelay >= TimeSpan.Zero
               && option.EventRetention > TimeSpan.Zero
               && option.DeliveryRetention > TimeSpan.Zero
               && option.DeliveryAttemptRetention > TimeSpan.Zero
               && option.ProcessedOutboxRetention > TimeSpan.Zero
               && option.AuditLogRetention > TimeSpan.Zero;

        public static bool IsNatsSettingsValid(NatsOption option)
        {
            if (string.IsNullOrWhiteSpace(option.Url)
                || string.IsNullOrWhiteSpace(option.Stream)
                || option.ConnectTimeout <= TimeSpan.Zero
                || option.PublishAckTimeout <= TimeSpan.Zero
                || option.DuplicateWindow <= TimeSpan.Zero)
            {
                return false;
            }

            var hasToken = !string.IsNullOrWhiteSpace(option.Token);
            var hasUser = !string.IsNullOrWhiteSpace(option.User);
            var hasPassword = !string.IsNullOrWhiteSpace(option.Password);
            var hasCredsFile = !string.IsNullOrWhiteSpace(option.CredsFile);

            if (hasUser != hasPassword)
            {
                return false;
            }

            var modes = 0;
            if (hasToken) modes++;
            if (hasUser && hasPassword) modes++;
            if (hasCredsFile) modes++;

            return modes <= 1;
        }
    }
}
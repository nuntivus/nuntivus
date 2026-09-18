using Microsoft.Extensions.Configuration;

namespace KairoServer.Infastructure.Configuration
{
    public static class KairoEnvironmentVariableConfiguration
    {
        public static void AddKairoEnvironmentVariableMappings(this ConfigurationManager configuration)
        {
            var mappedValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Database:PostgresConnectionString"] =
                    FirstNonEmpty(
                        Environment.GetEnvironmentVariable("KAIRO_DATABASE_CONNECTION_STRING"),
                        Environment.GetEnvironmentVariable("KAIRO_POSTGRES_CONNECTION_STRING")),

                ["ConnectionStrings:Postgres"] =
                    FirstNonEmpty(
                        Environment.GetEnvironmentVariable("KAIRO_DATABASE_CONNECTION_STRING"),
                        Environment.GetEnvironmentVariable("KAIRO_POSTGRES_CONNECTION_STRING")),

                ["DestinationCredentials:EncryptionKey"] =
                    Environment.GetEnvironmentVariable("KAIRO_DESTINATION_CREDENTIALS_KEY"),

                ["Nats:Url"] = Environment.GetEnvironmentVariable("KAIRO_NATS_URL"),
                ["Nats:Stream"] = Environment.GetEnvironmentVariable("KAIRO_NATS_STREAM"),
                ["Nats:Subjects"] = Environment.GetEnvironmentVariable("KAIRO_NATS_SUBJECTS"),
                ["OpenTelemetry:OtlpEndpoint"] = Environment.GetEnvironmentVariable("KAIRO_OTLP_ENDPOINT"),
            };

            var shutdownSeconds = Environment.GetEnvironmentVariable("KAIRO_SHUTDOWN_TIMEOUT_SECONDS");
            if (int.TryParse(shutdownSeconds, out var parsedSeconds) && parsedSeconds > 0)
            {
                mappedValues["HostOptions:ShutdownTimeout"] = TimeSpan.FromSeconds(parsedSeconds).ToString();
            }

            var filteredValues = mappedValues
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            if (filteredValues.Count > 0)
            {
                configuration.AddInMemoryCollection(filteredValues!);
            }
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
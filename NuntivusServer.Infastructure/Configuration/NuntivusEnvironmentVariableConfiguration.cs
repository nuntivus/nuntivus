using Microsoft.Extensions.Configuration;

namespace NuntivusServer.Infastructure.Configuration
{
    public static class NuntivusEnvironmentVariableConfiguration
    {
        public static void AddNuntivusEnvironmentVariableMappings(this ConfigurationManager configuration)
        {
            var mappedValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Database:PostgresConnectionString"] =
                    FirstNonEmpty(
                        Environment.GetEnvironmentVariable("NUNTIVUS_DATABASE_CONNECTION_STRING"),
                        Environment.GetEnvironmentVariable("NUNTIVUS_POSTGRES_CONNECTION_STRING")),

                ["ConnectionStrings:Postgres"] =
                    FirstNonEmpty(
                        Environment.GetEnvironmentVariable("NUNTIVUS_DATABASE_CONNECTION_STRING"),
                        Environment.GetEnvironmentVariable("NUNTIVUS_POSTGRES_CONNECTION_STRING")),

                ["DestinationCredentials:EncryptionKey"] =
                    Environment.GetEnvironmentVariable("NUNTIVUS_DESTINATION_CREDENTIALS_KEY"),

                ["Nats:Url"] = Environment.GetEnvironmentVariable("NUNTIVUS_NATS_URL"),
                ["Nats:Stream"] = Environment.GetEnvironmentVariable("NUNTIVUS_NATS_STREAM"),
                ["Nats:Subjects"] = Environment.GetEnvironmentVariable("NUNTIVUS_NATS_SUBJECTS"),
                ["OpenTelemetry:OtlpEndpoint"] = Environment.GetEnvironmentVariable("NUNTIVUS_OTLP_ENDPOINT"),

                ["DatabaseMigrations:Mode"] = Environment.GetEnvironmentVariable("NUNTIVUS_DATABASE_MIGRATIONS_MODE"),
                ["DatabaseMigrations:MigrationsPath"] = Environment.GetEnvironmentVariable("NUNTIVUS_DATABASE_MIGRATIONS_PATH"),
            };

            var shutdownSeconds = Environment.GetEnvironmentVariable("NUNTIVUS_SHUTDOWN_TIMEOUT_SECONDS");
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
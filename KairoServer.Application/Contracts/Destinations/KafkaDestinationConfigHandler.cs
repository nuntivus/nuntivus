using System;
using System.Text.Json;
using KairoServer.Application.Security;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.Application.Contracts.Destinations
{
    public sealed class KafkaDestinationConfigHandler : IDestinationConfigHandler
    {
        public const int MinTimeoutSeconds = 1;
        public const int MaxTimeoutSeconds = 300;

        private readonly DestinationCredentialNormalizer _credentialNormalizer;

        public KafkaDestinationConfigHandler(IDestinationCredentialProtector protector)
        {
            _credentialNormalizer = new DestinationCredentialNormalizer(
                protector ?? throw new ArgumentNullException(nameof(protector)));
        }

        public Type ConfigType => typeof(KafkaDestinationConfig);
        public DomainDestinationType DestinationType => DomainDestinationType.Kafka;
        public EndpointSecurityContext SecurityContext =>
            new(EndpointDirection.Destination, "kafka", "Destination");

        public Result<Destination> Build(DestinationConfig configuration)
        {
            if (configuration is not KafkaDestinationConfig kafka)
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Expected Kafka destination configuration, got '{configuration?.GetType().Name ?? "null"}'."));
            }

            if (string.IsNullOrWhiteSpace(kafka.BootstrapServers))
                return Result.Failure<Destination>(Error.Validation("Destination bootstrapServers is required."));

            if (string.IsNullOrWhiteSpace(kafka.Topic))
                return Result.Failure<Destination>(Error.Validation("Destination topic is required."));

            if (kafka.TimeoutSeconds is int timeout
                && (timeout < MinTimeoutSeconds || timeout > MaxTimeoutSeconds))
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Destination timeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}."));
            }

            if (!string.IsNullOrWhiteSpace(kafka.Acks)
                && !IsSupportedAcks(kafka.Acks))
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination acks must be one of: all, 1, 0, -1."));
            }

            var passwordResult = _credentialNormalizer.Normalize(
                kafka.Credentials?.Password,
                "Destination credentials.password");
            if (passwordResult.IsFailure)
                return Result<Destination>.Fail(passwordResult.Errors);

            var normalized = new KafkaDestinationConfig(
                kafka.BootstrapServers.Trim(),
                kafka.Topic.Trim(),
                string.IsNullOrWhiteSpace(kafka.ClientId) ? null : kafka.ClientId.Trim(),
                string.IsNullOrWhiteSpace(kafka.Acks) ? null : kafka.Acks.Trim().ToLowerInvariant(),
                kafka.TimeoutSeconds,
                kafka.Credentials is null
                    ? null
                    : new KafkaCredentials(
                        string.IsNullOrWhiteSpace(kafka.Credentials.SecurityProtocol) ? null : kafka.Credentials.SecurityProtocol.Trim(),
                        string.IsNullOrWhiteSpace(kafka.Credentials.SaslMechanism) ? null : kafka.Credentials.SaslMechanism.Trim(),
                        string.IsNullOrWhiteSpace(kafka.Credentials.Username) ? null : kafka.Credentials.Username.Trim(),
                        passwordResult.Value));

            return Result.Success(new Destination(DomainDestinationType.Kafka, JsonSerializer.Serialize(normalized)));
        }

        public Result<string> Redact(string configurationJson)
        {
            try
            {
                var config = JsonSerializer.Deserialize<KafkaDestinationConfig>(configurationJson);
                if (config is null)
                    return Result.Failure<string>(Error.Validation("Destination configuration is invalid."));

                var redacted = config with
                {
                    Credentials = config.Credentials is null
                        ? null
                        : config.Credentials with
                        {
                            Password = DestinationCredentialNormalizer.Redact(config.Credentials.Password)
                        }
                };

                return Result.Success(JsonSerializer.Serialize(redacted));
            }
            catch (JsonException ex)
            {
                return Result.Failure<string>(Error.Validation($"Destination configuration is invalid JSON: {ex.Message}"));
            }
        }

        public Result<Destination> Rotate(Destination current, Destination next) => Result.Success(next);

        private static bool IsSupportedAcks(string value)
        {
            var normalized = value.Trim().ToLowerInvariant();
            return normalized is "all" or "1" or "0" or "-1";
        }
    }
}

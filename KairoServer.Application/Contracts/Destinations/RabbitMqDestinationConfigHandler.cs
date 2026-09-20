using System;
using System.Text.Json;
using KairoServer.Application.Security;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.Application.Contracts.Destinations
{
    public sealed class RabbitMqDestinationConfigHandler : IDestinationConfigHandler
    {
        public const int MinTimeoutSeconds = 1;
        public const int MaxTimeoutSeconds = 300;

        private readonly DestinationCredentialNormalizer _credentialNormalizer;

        public RabbitMqDestinationConfigHandler(IDestinationCredentialProtector protector)
        {
            _credentialNormalizer = new DestinationCredentialNormalizer(
                protector ?? throw new ArgumentNullException(nameof(protector)));
        }

        public Type ConfigType => typeof(RabbitMqDestinationConfig);
        public DomainDestinationType DestinationType => DomainDestinationType.RabbitMq;
        public EndpointSecurityContext SecurityContext =>
            new(EndpointDirection.Destination, "rabbitmq", "Destination");

        public Result<Destination> Build(DestinationConfig configuration)
        {
            if (configuration is not RabbitMqDestinationConfig rabbitMq)
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Expected RabbitMQ destination configuration, got '{configuration?.GetType().Name ?? "null"}'."));
            }

            if (string.IsNullOrWhiteSpace(rabbitMq.Uri))
                return Result.Failure<Destination>(Error.Validation("Destination uri is required."));

            if (!Uri.TryCreate(rabbitMq.Uri, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("amqp" or "amqps"))
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination uri must be an absolute AMQP URL using amqp:// or amqps://."));
            }

            if (string.IsNullOrWhiteSpace(rabbitMq.Exchange))
                return Result.Failure<Destination>(Error.Validation("Destination exchange is required."));

            if (string.IsNullOrWhiteSpace(rabbitMq.RoutingKey))
                return Result.Failure<Destination>(Error.Validation("Destination routingKey is required."));

            if (rabbitMq.TimeoutSeconds is int timeout
                && (timeout < MinTimeoutSeconds || timeout > MaxTimeoutSeconds))
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Destination timeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}."));
            }

            var passwordResult = _credentialNormalizer.Normalize(
                rabbitMq.Credentials?.Password,
                "Destination credentials.password");
            if (passwordResult.IsFailure)
                return Result<Destination>.Fail(passwordResult.Errors);

            var normalized = new RabbitMqDestinationConfig(
                rabbitMq.Uri.Trim(),
                rabbitMq.Exchange.Trim(),
                rabbitMq.RoutingKey.Trim(),
                rabbitMq.Persistent,
                rabbitMq.Mandatory,
                rabbitMq.TimeoutSeconds,
                rabbitMq.Credentials is null
                    ? null
                    : new RabbitMqCredentials(
                        string.IsNullOrWhiteSpace(rabbitMq.Credentials.Username) ? null : rabbitMq.Credentials.Username.Trim(),
                        passwordResult.Value));

            return Result.Success(new Destination(DomainDestinationType.RabbitMq, JsonSerializer.Serialize(normalized)));
        }

        public Result<string> Redact(string configurationJson)
        {
            try
            {
                var config = JsonSerializer.Deserialize<RabbitMqDestinationConfig>(configurationJson);
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
    }
}

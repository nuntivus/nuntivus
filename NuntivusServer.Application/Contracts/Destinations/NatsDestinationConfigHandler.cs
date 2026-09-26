using System;
using System.Text.Json;
using NuntivusServer.Application.Security;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using DomainDestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Application.Contracts.Destinations
{
    public sealed class NatsDestinationConfigHandler : IDestinationConfigHandler
    {
        public const int MinTimeoutSeconds = 1;
        public const int MaxTimeoutSeconds = 300;

        private readonly DestinationCredentialNormalizer _credentialNormalizer;

        public NatsDestinationConfigHandler(IDestinationCredentialProtector protector)
        {
            _credentialNormalizer = new DestinationCredentialNormalizer(
                protector ?? throw new ArgumentNullException(nameof(protector)));
        }

        public Type ConfigType => typeof(NatsDestinationConfig);
        public DomainDestinationType DestinationType => DomainDestinationType.Nats;
        public EndpointSecurityContext SecurityContext =>
            new(EndpointDirection.Destination, "nats", "Destination");

        public Result<Destination> Build(DestinationConfig configuration)
        {
            if (configuration is not NatsDestinationConfig nats)
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Expected NATS destination configuration, got '{configuration?.GetType().Name ?? "null"}'."));
            }

            if (string.IsNullOrWhiteSpace(nats.Url))
                return Result.Failure<Destination>(Error.Validation("Destination url is required."));

            if (!Uri.TryCreate(nats.Url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("nats" or "tls"))
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination url must be an absolute NATS URL using nats:// or tls://."));
            }

            if (string.IsNullOrWhiteSpace(nats.Subject))
                return Result.Failure<Destination>(Error.Validation("Destination subject is required."));

            if (nats.TimeoutSeconds is int timeout
                && (timeout < MinTimeoutSeconds || timeout > MaxTimeoutSeconds))
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Destination timeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}."));
            }

            var tokenResult = _credentialNormalizer.Normalize(
                nats.Credentials?.Token,
                "Destination credentials.token");
            if (tokenResult.IsFailure)
                return Result<Destination>.Fail(tokenResult.Errors);

            var passwordResult = _credentialNormalizer.Normalize(
                nats.Credentials?.Password,
                "Destination credentials.password");
            if (passwordResult.IsFailure)
                return Result<Destination>.Fail(passwordResult.Errors);

            var normalized = new NatsDestinationConfig(
                nats.Url.Trim(),
                nats.Subject.Trim(),
                nats.TimeoutSeconds,
                nats.UseJetStream,
                string.IsNullOrWhiteSpace(nats.Stream) ? null : nats.Stream.Trim(),
                nats.Credentials is null
                    ? null
                    : new NatsCredentials(
                        tokenResult.Value,
                        string.IsNullOrWhiteSpace(nats.Credentials.Username) ? null : nats.Credentials.Username.Trim(),
                        passwordResult.Value));

            return Result.Success(new Destination(DomainDestinationType.Nats, JsonSerializer.Serialize(normalized)));
        }

        public Result<string> Redact(string configurationJson)
        {
            try
            {
                var config = JsonSerializer.Deserialize<NatsDestinationConfig>(configurationJson);
                if (config is null)
                    return Result.Failure<string>(Error.Validation("Destination configuration is invalid."));

                var redacted = config with
                {
                    Credentials = config.Credentials is null
                        ? null
                        : config.Credentials with
                        {
                            Token = DestinationCredentialNormalizer.Redact(config.Credentials.Token),
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

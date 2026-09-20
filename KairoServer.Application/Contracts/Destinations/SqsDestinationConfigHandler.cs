using System;
using System.Text.Json;
using KairoServer.Application.Security;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.Application.Contracts.Destinations
{
    public sealed class SqsDestinationConfigHandler : IDestinationConfigHandler
    {
        public const int MinTimeoutSeconds = 1;
        public const int MaxTimeoutSeconds = 300;

        private readonly DestinationCredentialNormalizer _credentialNormalizer;

        public SqsDestinationConfigHandler(IDestinationCredentialProtector protector)
        {
            _credentialNormalizer = new DestinationCredentialNormalizer(
                protector ?? throw new ArgumentNullException(nameof(protector)));
        }

        public Type ConfigType => typeof(SqsDestinationConfig);
        public DomainDestinationType DestinationType => DomainDestinationType.Sqs;
        public EndpointSecurityContext SecurityContext =>
            new(EndpointDirection.Destination, "sqs", "Destination");

        public Result<Destination> Build(DestinationConfig configuration)
        {
            if (configuration is not SqsDestinationConfig sqs)
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Expected SQS destination configuration, got '{configuration?.GetType().Name ?? "null"}'."));
            }

            if (string.IsNullOrWhiteSpace(sqs.Region))
                return Result.Failure<Destination>(Error.Validation("Destination region is required."));

            if (string.IsNullOrWhiteSpace(sqs.QueueUrl))
                return Result.Failure<Destination>(Error.Validation("Destination queueUrl is required."));

            if (!Uri.TryCreate(sqs.QueueUrl, UriKind.Absolute, out _))
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination queueUrl must be an absolute URL."));
            }

            if (!string.IsNullOrWhiteSpace(sqs.ServiceUrl)
                && !Uri.TryCreate(sqs.ServiceUrl, UriKind.Absolute, out _))
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination serviceUrl must be an absolute URL when provided."));
            }

            if (sqs.TimeoutSeconds is int timeout
                && (timeout < MinTimeoutSeconds || timeout > MaxTimeoutSeconds))
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Destination timeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}."));
            }

            var secretResult = _credentialNormalizer.Normalize(
                sqs.Credentials?.SecretAccessKey,
                "Destination credentials.secretAccessKey");
            if (secretResult.IsFailure)
                return Result<Destination>.Fail(secretResult.Errors);

            var sessionResult = _credentialNormalizer.Normalize(
                sqs.Credentials?.SessionToken,
                "Destination credentials.sessionToken");
            if (sessionResult.IsFailure)
                return Result<Destination>.Fail(sessionResult.Errors);

            var normalized = new SqsDestinationConfig(
                sqs.Region.Trim(),
                sqs.QueueUrl.Trim(),
                string.IsNullOrWhiteSpace(sqs.ServiceUrl) ? null : sqs.ServiceUrl.Trim(),
                sqs.TimeoutSeconds,
                sqs.Credentials is null
                    ? null
                    : new SqsCredentials(
                        string.IsNullOrWhiteSpace(sqs.Credentials.AccessKeyId) ? null : sqs.Credentials.AccessKeyId.Trim(),
                        secretResult.Value,
                        sessionResult.Value));

            return Result.Success(new Destination(DomainDestinationType.Sqs, JsonSerializer.Serialize(normalized)));
        }

        public Result<string> Redact(string configurationJson)
        {
            try
            {
                var config = JsonSerializer.Deserialize<SqsDestinationConfig>(configurationJson);
                if (config is null)
                    return Result.Failure<string>(Error.Validation("Destination configuration is invalid."));

                var redacted = config with
                {
                    Credentials = config.Credentials is null
                        ? null
                        : config.Credentials with
                        {
                            SecretAccessKey = DestinationCredentialNormalizer.Redact(config.Credentials.SecretAccessKey),
                            SessionToken = DestinationCredentialNormalizer.Redact(config.Credentials.SessionToken)
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

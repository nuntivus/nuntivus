using System;
using System.Linq;
using System.Text.Json;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.Application.Contracts.Destinations
{
    public sealed class DestinationConfigUpdateResolver : IDestinationConfigUpdateResolver
    {
        public Result<DestinationConfig> Resolve(DestinationConfig requested, Destination current)
        {
            if (requested is null || current is null)
                return Result.Failure<DestinationConfig>(Error.Validation("Destination configuration is required."));

            try
            {
                return requested switch
                {
                    HttpDestinationConfig http when current.Type == DomainDestinationType.Http =>
                        ResolveHttp(http, JsonSerializer.Deserialize<HttpDestinationConfig>(current.Configuration)!),
                    KafkaDestinationConfig kafka when current.Type == DomainDestinationType.Kafka =>
                        ResolveKafka(kafka, JsonSerializer.Deserialize<KafkaDestinationConfig>(current.Configuration)!),
                    NatsDestinationConfig nats when current.Type == DomainDestinationType.Nats =>
                        ResolveNats(nats, JsonSerializer.Deserialize<NatsDestinationConfig>(current.Configuration)!),
                    RabbitMqDestinationConfig rabbit when current.Type == DomainDestinationType.RabbitMq =>
                        ResolveRabbit(rabbit, JsonSerializer.Deserialize<RabbitMqDestinationConfig>(current.Configuration)!),
                    SqsDestinationConfig sqs when current.Type == DomainDestinationType.Sqs =>
                        ResolveSqs(sqs, JsonSerializer.Deserialize<SqsDestinationConfig>(current.Configuration)!),
                    _ => HasMaskedCredential(requested)
                        ? Result.Failure<DestinationConfig>(Error.Validation(
                            "Masked credentials cannot be reused for a different destination type."))
                        : Result.Success(requested)
                };
            }
            catch (JsonException)
            {
                return Result.Failure<DestinationConfig>(Error.Validation("Stored destination configuration is invalid."));
            }
        }

        private static Result<DestinationConfig> ResolveHttp(HttpDestinationConfig requested, HttpDestinationConfig stored)
        {
            var credential = requested.Authentication?.Credential;
            if (IsMasked(credential?.Value))
            {
                if (stored.Authentication?.Credential is null)
                    return MissingCredential();
                credential = stored.Authentication.Credential;
            }

            if (requested.SecretHeaders is not null && requested.SecretHeaders.Values.Any(IsMasked))
                return Result.Failure<DestinationConfig>(Error.Validation("Masked secret headers cannot be submitted."));

            if (requested.ProtectedHeaders is not null && requested.ProtectedHeaders.Values.Any(IsMasked))
                return Result.Failure<DestinationConfig>(Error.Validation("Masked protected headers cannot be submitted."));

            return Result.Success<DestinationConfig>(requested with
            {
                Authentication = requested.Authentication is null
                    ? null!
                    : requested.Authentication with { Credential = credential },
                Headers = requested.Headers ?? stored.Headers,
                SecretHeaders = requested.SecretHeaders,
                ProtectedHeaders = requested.ProtectedHeaders ?? stored.ProtectedHeaders
            });
        }

        private static Result<DestinationConfig> ResolveKafka(KafkaDestinationConfig requested, KafkaDestinationConfig stored)
        {
            var password = Restore(requested.Credentials?.Password, stored.Credentials?.Password);
            return password.IsFailure
                ? Result<DestinationConfig>.Fail(password.Errors)
                : Result.Success<DestinationConfig>(requested with
                {
                    Credentials = requested.Credentials is null ? null : requested.Credentials with { Password = password.Value }
                });
        }

        private static Result<DestinationConfig> ResolveNats(NatsDestinationConfig requested, NatsDestinationConfig stored)
        {
            var token = Restore(requested.Credentials?.Token, stored.Credentials?.Token);
            if (token.IsFailure) return Result<DestinationConfig>.Fail(token.Errors);
            var password = Restore(requested.Credentials?.Password, stored.Credentials?.Password);
            if (password.IsFailure) return Result<DestinationConfig>.Fail(password.Errors);
            return Result.Success<DestinationConfig>(requested with
            {
                Credentials = requested.Credentials is null ? null : requested.Credentials with
                {
                    Token = token.Value,
                    Password = password.Value
                }
            });
        }

        private static Result<DestinationConfig> ResolveRabbit(RabbitMqDestinationConfig requested, RabbitMqDestinationConfig stored)
        {
            var password = Restore(requested.Credentials?.Password, stored.Credentials?.Password);
            return password.IsFailure
                ? Result<DestinationConfig>.Fail(password.Errors)
                : Result.Success<DestinationConfig>(requested with
                {
                    Credentials = requested.Credentials is null ? null : requested.Credentials with { Password = password.Value }
                });
        }

        private static Result<DestinationConfig> ResolveSqs(SqsDestinationConfig requested, SqsDestinationConfig stored)
        {
            var secret = Restore(requested.Credentials?.SecretAccessKey, stored.Credentials?.SecretAccessKey);
            if (secret.IsFailure) return Result<DestinationConfig>.Fail(secret.Errors);
            var session = Restore(requested.Credentials?.SessionToken, stored.Credentials?.SessionToken);
            if (session.IsFailure) return Result<DestinationConfig>.Fail(session.Errors);
            return Result.Success<DestinationConfig>(requested with
            {
                Credentials = requested.Credentials is null ? null : requested.Credentials with
                {
                    SecretAccessKey = secret.Value,
                    SessionToken = session.Value
                }
            });
        }

        private static Result<DestinationCredential?> Restore(DestinationCredential? requested, DestinationCredential? stored) =>
            IsMasked(requested?.Value)
                ? stored is null
                    ? Result.Failure<DestinationCredential?>(Error.Validation("Masked credential has no stored value."))
                    : Result.Success<DestinationCredential?>(stored)
                : Result.Success(requested);

        private static bool IsMasked(string? value) => value is "***" or "[redacted]";

        private static bool HasMaskedCredential(DestinationConfig requested) => requested switch
        {
            HttpDestinationConfig http => IsMasked(http.Authentication?.Credential?.Value),
            KafkaDestinationConfig kafka => IsMasked(kafka.Credentials?.Password?.Value),
            NatsDestinationConfig nats => IsMasked(nats.Credentials?.Token?.Value)
                || IsMasked(nats.Credentials?.Password?.Value),
            RabbitMqDestinationConfig rabbit => IsMasked(rabbit.Credentials?.Password?.Value),
            SqsDestinationConfig sqs => IsMasked(sqs.Credentials?.SecretAccessKey?.Value)
                || IsMasked(sqs.Credentials?.SessionToken?.Value),
            _ => false
        };

        private static Result<DestinationConfig> MissingCredential() =>
            Result.Failure<DestinationConfig>(Error.Validation("Masked credential has no stored value."));
    }
}

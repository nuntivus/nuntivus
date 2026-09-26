using Confluent.Kafka;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Observability;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.CustomerContracts;
using DomainDestinationType = NuntivusServer.Domain.Contracts.DestinationType;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NuntivusServer.Infastructure.Deliveries.Kafka
{
    public sealed class KafkaDeliveryExecutor : IDeliveryExecutor, IDisposable
    {
        private static readonly ConcurrentDictionary<string, IProducer<Null, string>> Producers = new();

        private readonly ISecretResolver _secretResolver;
        private readonly IAppLogger<KafkaDeliveryExecutor> _logger;

        public KafkaDeliveryExecutor(
            ISecretResolver secretResolver,
            IAppLogger<KafkaDeliveryExecutor> logger)
        {
            _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public DomainDestinationType DestinationType => DomainDestinationType.Kafka;

        public async Task<DeliveryExecutionResult> ExecuteAsync(
            DeliveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            using var activity = NuntivusTelemetry.ActivitySource.StartActivity("delivery.kafka.execute", ActivityKind.Producer);
            activity?.SetTag(NuntivusTelemetry.DeliveryIdTag, request.DeliveryId.ToString("D"));
            activity?.SetTag(NuntivusTelemetry.EventIdTag, request.EventId.ToString("D"));
            activity?.SetTag(NuntivusTelemetry.ContractIdTag, request.ContractId.ToString("D"));
            activity?.SetTag(NuntivusTelemetry.CustomerIdTag, request.CustomerId);

            KafkaDestinationConfig config;
            try
            {
                var parsed = JsonSerializer.Deserialize<KafkaDestinationConfig>(request.Destination.Configuration);
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.BootstrapServers) || string.IsNullOrWhiteSpace(parsed.Topic))
                {
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        "Kafka destination configuration is empty or missing bootstrapServers/topic.");
                }

                config = parsed;
            }
            catch (JsonException ex)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"Kafka destination configuration is not valid JSON: {ex.Message}");
            }

            var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds is > 0 ? config.TimeoutSeconds.Value : 10);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var password = await ResolveCredentialAsync(config.Credentials?.Password, timeoutCts.Token);
            if (password.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    password.Error!.Message);
            }

            var producerResult = BuildProducer(config, password.Value);
            if (producerResult.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    producerResult.Error!.Message);
            }

            try
            {
                var message = new Message<Null, string>
                {
                    Value = request.Payload,
                    Headers = new Headers
                    {
                        { "x-nuntivus-delivery-id", System.Text.Encoding.UTF8.GetBytes(request.DeliveryId.ToString("D")) },
                        { "x-nuntivus-event-id", System.Text.Encoding.UTF8.GetBytes(request.EventId.ToString("D")) },
                        { "x-nuntivus-contract-id", System.Text.Encoding.UTF8.GetBytes(request.ContractId.ToString("D")) },
                        { "x-nuntivus-customer-id", System.Text.Encoding.UTF8.GetBytes(request.CustomerId) },
                        { "x-nuntivus-event-type", System.Text.Encoding.UTF8.GetBytes(request.EventType) },
                        { "x-nuntivus-attempt", System.Text.Encoding.UTF8.GetBytes(request.AttemptNumber.ToString()) },
                    }
                };

                var delivery = await producerResult.Value.ProduceAsync(config.Topic.Trim(), message, timeoutCts.Token);
                var metadata = new Dictionary<string, string>
                {
                    ["topic"] = delivery.Topic,
                    ["partition"] = delivery.Partition.Value.ToString(),
                    ["offset"] = delivery.Offset.Value.ToString(),
                    ["status"] = delivery.Status.ToString(),
                };

                return DeliveryExecutionResult.Success(metadata);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Timeout,
                    $"Kafka publish timed out after {timeout.TotalSeconds:0} seconds.");
            }
            catch (ProduceException<Null, string> ex)
            {
                var (category, retryAfter) = Classify(ex.Error.Code);
                return DeliveryExecutionResult.Failure(category, $"Kafka publish failed: {ex.Error.Reason}", retryAfter);
            }
            catch (KafkaException ex)
            {
                _logger.Warning("Kafka publish failed for delivery {DeliveryId}: {Error}.", request.DeliveryId, ex.Message);
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient,
                    $"Kafka publish failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected Kafka publish failure for delivery {DeliveryId}.", request.DeliveryId);
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient,
                    "Unexpected Kafka publish failure.");
            }
        }

        public void Dispose()
        {
            foreach (var producer in Producers.Values)
            {
                producer.Dispose();
            }

            Producers.Clear();
        }

        private Result<IProducer<Null, string>> BuildProducer(KafkaDestinationConfig config, string? password)
        {
            try
            {
                var producerConfig = new ProducerConfig
                {
                    BootstrapServers = config.BootstrapServers.Trim(),
                    ClientId = string.IsNullOrWhiteSpace(config.ClientId) ? "nuntivus-delivery" : config.ClientId.Trim(),
                    Acks = ParseAcks(config.Acks),
                    MessageTimeoutMs = (config.TimeoutSeconds is > 0 ? config.TimeoutSeconds.Value : 10) * 1000,
                };

                if (!string.IsNullOrWhiteSpace(config.Credentials?.Username))
                {
                    producerConfig.SaslUsername = config.Credentials.Username.Trim();
                    producerConfig.SaslPassword = password;
                    producerConfig.SecurityProtocol = ParseSecurityProtocol(config.Credentials.SecurityProtocol);
                    producerConfig.SaslMechanism = ParseSaslMechanism(config.Credentials.SaslMechanism);
                }

                var cacheKey = BuildCacheKey(producerConfig);
                var producer = Producers.GetOrAdd(cacheKey, _ =>
                    new ProducerBuilder<Null, string>(producerConfig).Build());

                return Result<IProducer<Null, string>>.Ok(producer);
            }
            catch (Exception ex)
            {
                return Result<IProducer<Null, string>>.Fail(NuntivusServer.Common.Error.Validation(
                    $"Kafka producer configuration is invalid: {ex.Message}"));
            }
        }

        private async Task<Result<string?>> ResolveCredentialAsync(
            DestinationCredential? credential,
            CancellationToken cancellationToken)
        {
            if (credential is null)
                return Result.Success<string?>(null);

            if (string.Equals(credential.Kind, "ref", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = await _secretResolver.ResolveAsync(credential.Value, cancellationToken);
                if (resolved.IsFailure)
                    return Result.Failure<string?>(resolved.Error!);

                return Result.Success<string?>(resolved.Value);
            }

            if (string.Equals(credential.Kind, "inline", StringComparison.OrdinalIgnoreCase))
                return Result.Success<string?>(credential.Value);

            return Result.Failure<string?>(NuntivusServer.Common.Error.Validation("Credential kind must be 'ref' or 'inline'."));
        }

        private static (DeliveryFailureCategory Category, TimeSpan? RetryAfter) Classify(ErrorCode code) => code switch
        {
            ErrorCode.Local_MsgTimedOut => (DeliveryFailureCategory.Timeout, null),
            ErrorCode.Local_QueueFull => (DeliveryFailureCategory.Throttled, TimeSpan.FromSeconds(1)),
            ErrorCode.TopicAuthorizationFailed => (DeliveryFailureCategory.Permanent, null),
            ErrorCode.GroupAuthorizationFailed => (DeliveryFailureCategory.Permanent, null),
            ErrorCode.ClusterAuthorizationFailed => (DeliveryFailureCategory.Permanent, null),
            ErrorCode.UnknownTopicOrPart => (DeliveryFailureCategory.Permanent, null),
            ErrorCode.InvalidConfig => (DeliveryFailureCategory.ConfigurationInvalid, null),
            _ => (DeliveryFailureCategory.Transient, null),
        };

        private static string BuildCacheKey(ProducerConfig config) =>
            string.Join("|",
                config.BootstrapServers ?? string.Empty,
                config.ClientId ?? string.Empty,
                config.SecurityProtocol?.ToString() ?? string.Empty,
                config.SaslMechanism?.ToString() ?? string.Empty,
                config.SaslUsername ?? string.Empty,
                config.SaslPassword ?? string.Empty,
                config.Acks.ToString());

        private static Acks ParseAcks(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "0" => Acks.None,
                "1" => Acks.Leader,
                "-1" => Acks.All,
                "all" => Acks.All,
                _ => Acks.All,
            };
        }

        private static SecurityProtocol ParseSecurityProtocol(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "plaintext" => SecurityProtocol.Plaintext,
                "ssl" => SecurityProtocol.Ssl,
                "sasl_plaintext" => SecurityProtocol.SaslPlaintext,
                "sasl_ssl" => SecurityProtocol.SaslSsl,
                _ => SecurityProtocol.SaslPlaintext,
            };
        }

        private static SaslMechanism ParseSaslMechanism(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "scram-sha-256" => SaslMechanism.ScramSha256,
                "scram-sha-512" => SaslMechanism.ScramSha512,
                _ => SaslMechanism.Plain,
            };
        }
    }
}

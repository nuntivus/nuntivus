using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Observability;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KairoServer.Infastructure.Deliveries.RabbitMq
{
    public sealed class RabbitMqDeliveryExecutor : IDeliveryExecutor, IDisposable
    {
        private static readonly ConcurrentDictionary<string, IConnection> Connections = new();

        private readonly ISecretResolver _secretResolver;
        private readonly IAppLogger<RabbitMqDeliveryExecutor> _logger;

        public RabbitMqDeliveryExecutor(
            ISecretResolver secretResolver,
            IAppLogger<RabbitMqDeliveryExecutor> logger)
        {
            _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public DomainDestinationType DestinationType => DomainDestinationType.RabbitMq;

        public async Task<DeliveryExecutionResult> ExecuteAsync(
            DeliveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            using var activity = KairoTelemetry.ActivitySource.StartActivity("delivery.rabbitmq.execute", ActivityKind.Producer);
            activity?.SetTag(KairoTelemetry.DeliveryIdTag, request.DeliveryId.ToString("D"));
            activity?.SetTag(KairoTelemetry.EventIdTag, request.EventId.ToString("D"));
            activity?.SetTag(KairoTelemetry.ContractIdTag, request.ContractId.ToString("D"));
            activity?.SetTag(KairoTelemetry.CustomerIdTag, request.CustomerId);

            RabbitMqDestinationConfig config;
            try
            {
                var parsed = JsonSerializer.Deserialize<RabbitMqDestinationConfig>(request.Destination.Configuration);
                if (parsed is null
                    || string.IsNullOrWhiteSpace(parsed.Uri)
                    || string.IsNullOrWhiteSpace(parsed.Exchange)
                    || string.IsNullOrWhiteSpace(parsed.RoutingKey))
                {
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        "RabbitMQ destination configuration is empty or missing uri/exchange/routingKey.");
                }

                config = parsed;
            }
            catch (JsonException ex)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"RabbitMQ destination configuration is not valid JSON: {ex.Message}");
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

            try
            {
                var connection = GetOrCreateConnection(config, password.Value);
                using var channel = connection.CreateModel();

                channel.ConfirmSelect();

                var properties = channel.CreateBasicProperties();
                properties.Persistent = config.Persistent;
                properties.ContentType = "application/json";
                properties.Headers = new Dictionary<string, object>
                {
                    ["x-kairo-delivery-id"] = request.DeliveryId.ToString("D"),
                    ["x-kairo-event-id"] = request.EventId.ToString("D"),
                    ["x-kairo-contract-id"] = request.ContractId.ToString("D"),
                    ["x-kairo-customer-id"] = request.CustomerId,
                    ["x-kairo-event-type"] = request.EventType,
                    ["x-kairo-attempt"] = request.AttemptNumber,
                };

                var body = Encoding.UTF8.GetBytes(request.Payload);
                channel.BasicPublish(
                    exchange: config.Exchange.Trim(),
                    routingKey: config.RoutingKey.Trim(),
                    mandatory: config.Mandatory,
                    basicProperties: properties,
                    body: body);

                var acknowledged = channel.WaitForConfirms(timeout);
                if (!acknowledged)
                {
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.Transient,
                        "RabbitMQ publish was not acknowledged by broker.");
                }

                var metadata = new Dictionary<string, string>
                {
                    ["exchange"] = config.Exchange.Trim(),
                    ["routingKey"] = config.RoutingKey.Trim(),
                    ["brokerAck"] = "true",
                };

                return DeliveryExecutionResult.Success(metadata);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Timeout,
                    $"RabbitMQ publish timed out after {timeout.TotalSeconds:0} seconds.");
            }
            catch (TimeoutException)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Timeout,
                    $"RabbitMQ publish timed out after {timeout.TotalSeconds:0} seconds.");
            }
            catch (BrokerUnreachableException ex)
            {
                _logger.Warning("RabbitMQ broker unreachable for delivery {DeliveryId}: {Error}.", request.DeliveryId, ex.Message);
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient,
                    $"RabbitMQ broker unreachable: {ex.Message}");
            }
            catch (OperationInterruptedException ex)
            {
                var code = ex.ShutdownReason?.ReplyCode ?? 0;
                var category = code is 403 or 404 ? DeliveryFailureCategory.Permanent : DeliveryFailureCategory.Transient;
                return DeliveryExecutionResult.Failure(category, $"RabbitMQ publish failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected RabbitMQ publish failure for delivery {DeliveryId}.", request.DeliveryId);
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient,
                    "Unexpected RabbitMQ publish failure.");
            }
        }

        public void Dispose()
        {
            foreach (var connection in Connections.Values)
            {
                connection.Dispose();
            }

            Connections.Clear();
        }

        private IConnection GetOrCreateConnection(RabbitMqDestinationConfig config, string? password)
        {
            var key = string.Join("|", config.Uri.Trim(), config.Credentials?.Username ?? string.Empty, password ?? string.Empty);
            return Connections.GetOrAdd(key, _ =>
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(config.Uri.Trim()),
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds is > 0 ? config.TimeoutSeconds.Value : 10),
                    AutomaticRecoveryEnabled = true,
                    TopologyRecoveryEnabled = true,
                };

                if (!string.IsNullOrWhiteSpace(config.Credentials?.Username))
                {
                    factory.UserName = config.Credentials.Username.Trim();
                    factory.Password = password;
                }

                return factory.CreateConnection("kairo-delivery");
            });
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

            return Result.Failure<string?>(Error.Validation("Credential kind must be 'ref' or 'inline'."));
        }
    }
}

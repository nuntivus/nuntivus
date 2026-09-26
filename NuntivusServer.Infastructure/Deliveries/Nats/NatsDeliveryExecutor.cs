using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Observability;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.CustomerContracts;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using DomainDestinationType = NuntivusServer.Domain.Contracts.DestinationType;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NuntivusServer.Infastructure.Deliveries.Nats
{
    public sealed class NatsDeliveryExecutor : IDeliveryExecutor
    {
        private static readonly ConcurrentDictionary<string, INatsConnection> Connections = new();

        private readonly ISecretResolver _secretResolver;
        private readonly IAppLogger<NatsDeliveryExecutor> _logger;

        public NatsDeliveryExecutor(
            ISecretResolver secretResolver,
            IAppLogger<NatsDeliveryExecutor> logger)
        {
            _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public DomainDestinationType DestinationType => DomainDestinationType.Nats;

        public async Task<DeliveryExecutionResult> ExecuteAsync(
            DeliveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            using var activity = NuntivusTelemetry.ActivitySource.StartActivity("delivery.nats.execute", ActivityKind.Producer);
            activity?.SetTag(NuntivusTelemetry.DeliveryIdTag, request.DeliveryId.ToString("D"));
            activity?.SetTag(NuntivusTelemetry.EventIdTag, request.EventId.ToString("D"));
            activity?.SetTag(NuntivusTelemetry.ContractIdTag, request.ContractId.ToString("D"));
            activity?.SetTag(NuntivusTelemetry.CustomerIdTag, request.CustomerId);

            NatsDestinationConfig config;
            try
            {
                var parsed = JsonSerializer.Deserialize<NatsDestinationConfig>(request.Destination.Configuration);
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.Url) || string.IsNullOrWhiteSpace(parsed.Subject))
                {
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        "NATS destination configuration is empty or missing url/subject.");
                }

                config = parsed;
            }
            catch (JsonException ex)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"NATS destination configuration is not valid JSON: {ex.Message}");
            }

            var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds is > 0 ? config.TimeoutSeconds.Value : 10);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var token = await ResolveCredentialAsync(config.Credentials?.Token, timeoutCts.Token);
            if (token.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    token.Error!.Message);
            }

            var password = await ResolveCredentialAsync(config.Credentials?.Password, timeoutCts.Token);
            if (password.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    password.Error!.Message);
            }

            try
            {
                var connection = GetOrCreateConnection(config, token.Value, password.Value);
                var jetStream = new NatsJSContext(connection);

                var ack = await jetStream.PublishAsync(
                    subject: config.Subject.Trim(),
                    data: Encoding.UTF8.GetBytes(request.Payload),
                    opts: new NatsJSPubOpts
                    {
                        MsgId = request.DeliveryId.ToString("D"),
                    },
                    cancellationToken: timeoutCts.Token);

                ack.EnsureSuccess();

                var metadata = new Dictionary<string, string>
                {
                    ["subject"] = config.Subject.Trim(),
                    ["stream"] = ack.Stream,
                    ["sequence"] = ack.Seq.ToString(),
                    ["duplicate"] = ack.Duplicate.ToString(),
                };

                return DeliveryExecutionResult.Success(metadata);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Timeout,
                    $"NATS publish timed out after {timeout.TotalSeconds:0} seconds.");
            }
            catch (Exception ex)
            {
                _logger.Warning("NATS publish failed for delivery {DeliveryId}: {Error}.", request.DeliveryId, ex.Message);
                var category = Classify(ex);
                return DeliveryExecutionResult.Failure(category, $"NATS publish failed: {ex.Message}");
            }
        }

        private INatsConnection GetOrCreateConnection(
            NatsDestinationConfig config,
            string? token,
            string? password)
        {
            var key = string.Join("|",
                config.Url.Trim(),
                config.Credentials?.Username ?? string.Empty,
                token ?? string.Empty,
                password ?? string.Empty);

            return Connections.GetOrAdd(key, _ =>
            {
                var opts = new NatsOpts
                {
                    Url = config.Url.Trim(),
                    ConnectTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds is > 0 ? config.TimeoutSeconds.Value : 10),
                    Name = "nuntivus-delivery",
                    AuthOpts = NatsAuthOpts.Default with
                    {
                        Token = token,
                        Username = config.Credentials?.Username,
                        Password = password,
                    },
                };

                return new NatsConnection(opts);
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

        private static DeliveryFailureCategory Classify(Exception ex)
        {
            var message = ex.Message;
            if (message.Contains("authorization", StringComparison.OrdinalIgnoreCase)
                || message.Contains("authentication", StringComparison.OrdinalIgnoreCase))
            {
                return DeliveryFailureCategory.Permanent;
            }

            return DeliveryFailureCategory.Transient;
        }
    }
}

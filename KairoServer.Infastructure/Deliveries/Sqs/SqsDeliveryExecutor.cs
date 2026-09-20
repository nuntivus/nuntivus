using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Observability;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KairoServer.Infastructure.Deliveries.Sqs
{
    public sealed class SqsDeliveryExecutor : IDeliveryExecutor, IDisposable
    {
        private static readonly ConcurrentDictionary<string, AmazonSQSClient> Clients = new();

        private readonly ISecretResolver _secretResolver;
        private readonly IAppLogger<SqsDeliveryExecutor> _logger;

        public SqsDeliveryExecutor(
            ISecretResolver secretResolver,
            IAppLogger<SqsDeliveryExecutor> logger)
        {
            _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public DomainDestinationType DestinationType => DomainDestinationType.Sqs;

        public async Task<DeliveryExecutionResult> ExecuteAsync(
            DeliveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            using var activity = KairoTelemetry.ActivitySource.StartActivity("delivery.sqs.execute", ActivityKind.Producer);
            activity?.SetTag(KairoTelemetry.DeliveryIdTag, request.DeliveryId.ToString("D"));
            activity?.SetTag(KairoTelemetry.EventIdTag, request.EventId.ToString("D"));
            activity?.SetTag(KairoTelemetry.ContractIdTag, request.ContractId.ToString("D"));
            activity?.SetTag(KairoTelemetry.CustomerIdTag, request.CustomerId);

            SqsDestinationConfig config;
            try
            {
                var parsed = JsonSerializer.Deserialize<SqsDestinationConfig>(request.Destination.Configuration);
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.Region) || string.IsNullOrWhiteSpace(parsed.QueueUrl))
                {
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        "SQS destination configuration is empty or missing region/queueUrl.");
                }

                config = parsed;
            }
            catch (JsonException ex)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"SQS destination configuration is not valid JSON: {ex.Message}");
            }

            var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds is > 0 ? config.TimeoutSeconds.Value : 10);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var secretAccessKey = await ResolveCredentialAsync(config.Credentials?.SecretAccessKey, timeoutCts.Token);
            if (secretAccessKey.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    secretAccessKey.Error!.Message);
            }

            var sessionToken = await ResolveCredentialAsync(config.Credentials?.SessionToken, timeoutCts.Token);
            if (sessionToken.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    sessionToken.Error!.Message);
            }

            try
            {
                var clientResult = BuildClient(config, secretAccessKey.Value, sessionToken.Value);
                if (clientResult.IsFailure)
                {
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        clientResult.Error!.Message);
                }

                var response = await clientResult.Value.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = config.QueueUrl.Trim(),
                    MessageBody = request.Payload,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        ["kairoDeliveryId"] = new MessageAttributeValue { DataType = "String", StringValue = request.DeliveryId.ToString("D") },
                        ["kairoEventId"] = new MessageAttributeValue { DataType = "String", StringValue = request.EventId.ToString("D") },
                        ["kairoContractId"] = new MessageAttributeValue { DataType = "String", StringValue = request.ContractId.ToString("D") },
                        ["kairoCustomerId"] = new MessageAttributeValue { DataType = "String", StringValue = request.CustomerId },
                        ["kairoEventType"] = new MessageAttributeValue { DataType = "String", StringValue = request.EventType },
                        ["kairoAttempt"] = new MessageAttributeValue { DataType = "Number", StringValue = request.AttemptNumber.ToString() },
                    },
                }, timeoutCts.Token);

                var metadata = new Dictionary<string, string>
                {
                    ["messageId"] = response.MessageId,
                    ["md5Body"] = response.MD5OfMessageBody,
                    ["requestId"] = response.ResponseMetadata?.RequestId ?? string.Empty,
                };

                return DeliveryExecutionResult.Success(metadata);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Timeout,
                    $"SQS publish timed out after {timeout.TotalSeconds:0} seconds.");
            }
            catch (QueueDoesNotExistException ex)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Permanent,
                    $"SQS queue does not exist: {ex.Message}");
            }
            catch (AmazonSQSException ex)
            {
                var category = Classify(ex);
                return DeliveryExecutionResult.Failure(category, $"SQS publish failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected SQS publish failure for delivery {DeliveryId}.", request.DeliveryId);
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient,
                    "Unexpected SQS publish failure.");
            }
        }

        public void Dispose()
        {
            foreach (var client in Clients.Values)
            {
                client.Dispose();
            }

            Clients.Clear();
        }

        private Result<AmazonSQSClient> BuildClient(
            SqsDestinationConfig config,
            string? secretAccessKey,
            string? sessionToken)
        {
            try
            {
                var sqsConfig = new AmazonSQSConfig
                {
                    RegionEndpoint = RegionEndpoint.GetBySystemName(config.Region.Trim()),
                };

                if (!string.IsNullOrWhiteSpace(config.ServiceUrl))
                {
                    sqsConfig.ServiceURL = config.ServiceUrl.Trim();
                    sqsConfig.UseHttp = sqsConfig.ServiceURL.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
                }

                var accessKeyId = config.Credentials?.AccessKeyId;
                AWSCredentials? credentials = null;
                if (!string.IsNullOrWhiteSpace(accessKeyId) && !string.IsNullOrWhiteSpace(secretAccessKey))
                {
                    credentials = string.IsNullOrWhiteSpace(sessionToken)
                        ? new BasicAWSCredentials(accessKeyId.Trim(), secretAccessKey)
                        : new SessionAWSCredentials(accessKeyId.Trim(), secretAccessKey, sessionToken);
                }

                var key = string.Join("|",
                    config.Region.Trim(),
                    config.ServiceUrl ?? string.Empty,
                    accessKeyId ?? string.Empty,
                    secretAccessKey ?? string.Empty,
                    sessionToken ?? string.Empty);

                var client = Clients.GetOrAdd(key, _ =>
                    credentials is null
                        ? new AmazonSQSClient(sqsConfig)
                        : new AmazonSQSClient(credentials, sqsConfig));

                return Result<AmazonSQSClient>.Ok(client);
            }
            catch (Exception ex)
            {
                return Result<AmazonSQSClient>.Fail(KairoServer.Common.Error.Validation(
                    $"SQS client configuration is invalid: {ex.Message}"));
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

            return Result.Failure<string?>(Error.Validation("Credential kind must be 'ref' or 'inline'."));
        }

        private static DeliveryFailureCategory Classify(AmazonSQSException ex)
        {
            if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                return DeliveryFailureCategory.Throttled;

            if ((int)ex.StatusCode >= 500)
                return DeliveryFailureCategory.Transient;

            if (string.Equals(ex.ErrorCode, "InvalidClientTokenId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ex.ErrorCode, "SignatureDoesNotMatch", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ex.ErrorCode, "AccessDenied", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ex.ErrorCode, "InvalidSecurity", StringComparison.OrdinalIgnoreCase))
            {
                return DeliveryFailureCategory.Permanent;
            }

            return DeliveryFailureCategory.Transient;
        }
    }
}

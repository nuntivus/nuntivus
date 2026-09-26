using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Observability;
using NuntivusServer.Application.Security;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.CustomerContracts;
using DomainDestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Infastructure.Deliveries.Http
{
    /// <summary>
    /// HTTP transport implementation of <see cref="IDeliveryExecutor"/>.
    /// Owns parsing and validating <see cref="HttpDestinationConfig"/> and
    /// injecting destination authentication into outbound requests.
    /// Credential resolution is centralized through <see cref="ISecretResolver"/>
    /// so auth storage concerns stay outside delivery orchestration.
    /// maps status codes / transport exceptions to
    /// <see cref="DeliveryFailureCategory"/>. Delivery Core stays HTTP-agnostic.
    /// </summary>
    public sealed class HttpDeliveryExecutor : IDeliveryExecutor
    {
        private static readonly MediaTypeHeaderValue JsonMediaType =
            new("application/json") { CharSet = "utf-8" };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ISecretResolver _secretResolver;
        private readonly IAppLogger<HttpDeliveryExecutor> _logger;
        private readonly HttpDestinationSecurityOption _security;
        private readonly IEndpointSecurityPolicy _securityPolicy;

        public const string HttpClientName = "nuntivus-delivery-http";
        private static readonly EndpointSecurityContext SecurityContext =
            new(EndpointDirection.Destination, "http", "Destination");

        public HttpDeliveryExecutor(
            IHttpClientFactory httpClientFactory,
            ISecretResolver secretResolver,
            IAppLogger<HttpDeliveryExecutor> logger,
            HttpDestinationSecurityOption? security = null,
            IEndpointSecurityPolicy? securityPolicy = null)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _security = security ?? new HttpDestinationSecurityOption();
            _securityPolicy = securityPolicy ?? new EndpointSecurityPolicy();
        }

        public DomainDestinationType DestinationType => DomainDestinationType.Http;

        /// <summary>
        /// Executes one HTTP delivery attempt end-to-end:
        /// parse destination config, inject headers/auth, send request,
        /// and classify the transport/protocol outcome.
        /// </summary>
        public async Task<DeliveryExecutionResult> ExecuteAsync(
            DeliveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            using var activity = NuntivusTelemetry.ActivitySource.StartActivity("delivery.http.execute", ActivityKind.Client);
            activity?.SetTag(NuntivusTelemetry.DeliveryIdTag, request.DeliveryId.ToString("D"));
            activity?.SetTag(NuntivusTelemetry.EventIdTag, request.EventId.ToString("D"));
            activity?.SetTag(NuntivusTelemetry.ContractIdTag, request.ContractId.ToString("D"));
            activity?.SetTag(NuntivusTelemetry.CustomerIdTag, request.CustomerId);

            HttpDestinationConfig config;
            try
            {
                var parsed = JsonSerializer.Deserialize<HttpDestinationConfig>(
                    request.Destination.Configuration);
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.Url))
                {
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        "HTTP destination configuration is empty or missing Url.");
                }
                config = parsed;
            }
            catch (JsonException ex)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"HTTP destination configuration is not valid JSON: {ex.Message}");
            }

            if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var destinationUri))
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    "Destination url must be an absolute URL.");
            }

            var destinationValidation = _securityPolicy.ValidateEndpointUri(
                destinationUri,
                SecurityContext,
                _security);
            if (destinationValidation.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    destinationValidation.Errors[0].Message);
            }

            var destinationAddressValidation = await ValidateDestinationAddressAsync(destinationUri, cancellationToken);
            if (destinationAddressValidation is not null)
                return destinationAddressValidation;

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, destinationUri)
            {
                Content = new StringContent(request.Payload, Encoding.UTF8),
            };
            httpRequest.Content.Headers.ContentType = JsonMediaType;

            httpRequest.Headers.TryAddWithoutValidation("X-Nuntivus-Delivery-Id", request.DeliveryId.ToString());
            httpRequest.Headers.TryAddWithoutValidation("X-Nuntivus-Event-Id", request.EventId.ToString());
            httpRequest.Headers.TryAddWithoutValidation("X-Nuntivus-Contract-Id", request.ContractId.ToString());
            httpRequest.Headers.TryAddWithoutValidation("X-Nuntivus-Customer-Id", request.CustomerId);
            httpRequest.Headers.TryAddWithoutValidation("X-Nuntivus-Event-Type", request.EventType);
            httpRequest.Headers.TryAddWithoutValidation("X-Nuntivus-Attempt", request.AttemptNumber.ToString());

            if (config.Headers is not null)
            {
                foreach (var kvp in config.Headers)
                {
                    httpRequest.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                }
            }

            if (config.SecretHeaders is not null)
            {
                foreach (var kvp in config.SecretHeaders)
                {
                    httpRequest.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                }
            }

            var protectedHeadersResult = await ApplyProtectedHeadersAsync(httpRequest, config, cancellationToken);
            if (protectedHeadersResult is not null)
                return protectedHeadersResult;

            var authResult = await ApplyAuthenticationAsync(httpRequest, config, cancellationToken);
            if (authResult is not null)
            {
                return authResult;
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds ?? 30);

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                var startedAt = Stopwatch.GetTimestamp();
                using var response = await client.SendAsync(httpRequest, linkedCts.Token);
                var elapsed = Stopwatch.GetElapsedTime(startedAt);
                activity?.SetTag("nuntivus.destination.latency.ms", elapsed.TotalMilliseconds);
                var result = await ClassifyResponseAsync(
                    response,
                    request,
                    elapsed,
                    _security.MaxResponseBodyBytes,
                    linkedCts.Token);
                activity?.SetTag(NuntivusTelemetry.OutcomeTag, result.Succeeded ? "delivered" : "failed");
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                activity?.SetTag(NuntivusTelemetry.OutcomeTag, "timeout");
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Timeout,
                    $"HTTP request timed out after {timeout.TotalSeconds}s.");
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning(
                    "HTTP delivery {DeliveryId} transport error: {Error}",
                    request.DeliveryId, ex.Message);
                activity?.SetTag(NuntivusTelemetry.OutcomeTag, "transport_error");
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient,
                    $"HTTP transport error: {ex.Message}");
            }
        }

        private async Task<DeliveryExecutionResult?> ApplyAuthenticationAsync(
            HttpRequestMessage httpRequest,
            HttpDestinationConfig config,
            CancellationToken cancellationToken)
        {
            // Authentication remains protocol-specific here (HTTP boundary),
            // while upstream layers pass only opaque destination config.
            var authType = config.Authentication?.Type?.Trim().ToLowerInvariant() ?? "none";
            if (authType == "none")
            {
                return null;
            }

            if (config.Authentication?.Credential is null)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"HTTP destination auth '{authType}' requires credentials.");
            }

            var credentialKind = config.Authentication.Credential.Kind?.Trim().ToLowerInvariant();
            var credentialValue = config.Authentication.Credential.Value?.Trim();
            if (string.IsNullOrWhiteSpace(credentialKind) || string.IsNullOrWhiteSpace(credentialValue))
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"HTTP destination auth '{authType}' has invalid credentials.");
            }

            switch (authType)
            {
                case "bearer":
                    var secretResult = await ResolveCredentialByKindAsync(
                        credentialKind,
                        credentialValue,
                        cancellationToken);
                    if (secretResult.IsFailure)
                    {
                        return DeliveryExecutionResult.Failure(
                            DeliveryFailureCategory.ConfigurationInvalid,
                            $"Failed to resolve credentials for HTTP auth '{authType}'.");
                    }

                    httpRequest.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", secretResult.Value);
                    return null;

                case "hmac":
                    return await ApplyHmacAuthenticationAsync(httpRequest, config, cancellationToken);

                default:
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        $"Unsupported HTTP auth type '{authType}'.");
            }
        }

        private async Task<DeliveryExecutionResult?> ApplyHmacAuthenticationAsync(
            HttpRequestMessage httpRequest,
            HttpDestinationConfig config,
            CancellationToken cancellationToken)
        {
            var credential = config.Authentication.Credential!;
            var credentialKind = credential.Kind?.Trim().ToLowerInvariant();
            var credentialValue = credential.Value?.Trim();

            if (string.IsNullOrWhiteSpace(credentialKind) || string.IsNullOrWhiteSpace(credentialValue))
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    "HTTP destination auth 'hmac' has invalid credentials.");
            }

            var primarySecretResult = await ResolveCredentialByKindAsync(
                credentialKind,
                credentialValue,
                cancellationToken);
            if (primarySecretResult.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    "Failed to resolve credentials for HTTP auth 'hmac'.");
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timestampValue = timestamp.ToString(CultureInfo.InvariantCulture);
            var payload = httpRequest.Content is null
                ? string.Empty
                : await httpRequest.Content.ReadAsStringAsync(cancellationToken);

            var activeSignature = ComputeHmacSignature(primarySecretResult.Value, payload, timestampValue);
            var activeVersion = credential.Version ?? 1;

            httpRequest.Headers.TryAddWithoutValidation("X-Nuntivus-Signature-Timestamp", timestampValue);
            httpRequest.Headers.TryAddWithoutValidation("X-Nuntivus-Signature", $"v{activeVersion}={activeSignature}");

            if (credential.Previous is null || credential.Previous.Count == 0)
                return null;

            var previousSignatures = new List<string>(credential.Previous.Count);
            foreach (var previous in credential.Previous)
            {
                var previousKind = previous.Kind?.Trim().ToLowerInvariant();
                var previousValue = previous.Value?.Trim();
                if (string.IsNullOrWhiteSpace(previousKind) || string.IsNullOrWhiteSpace(previousValue))
                    continue;

                var previousSecretResult = await ResolveCredentialByKindAsync(
                    previousKind,
                    previousValue,
                    cancellationToken);
                if (previousSecretResult.IsFailure)
                {
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        "Failed to resolve overlap credentials for HTTP auth 'hmac'.");
                }

                var previousVersion = previous.Version ?? 0;
                var previousSignature = ComputeHmacSignature(
                    previousSecretResult.Value,
                    payload,
                    timestampValue);
                previousSignatures.Add($"v{previousVersion}={previousSignature}");
            }

            if (previousSignatures.Count > 0)
            {
                httpRequest.Headers.TryAddWithoutValidation(
                    "X-Nuntivus-Signature-Previous",
                    string.Join(",", previousSignatures));
            }

            return null;
        }

        private async Task<DeliveryExecutionResult?> ApplyProtectedHeadersAsync(
            HttpRequestMessage httpRequest,
            HttpDestinationConfig config,
            CancellationToken cancellationToken)
        {
            // protectedHeaders contain references (not plaintext values).
            // Values are resolved just-in-time and never persisted as raw secrets.
            if (config.ProtectedHeaders is null)
                return null;

            foreach (var kvp in config.ProtectedHeaders)
            {
                var resolved = await ResolveCredentialAsync(kvp.Value, null, cancellationToken);
                if (resolved.IsFailure)
                {
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        $"Failed to resolve destination protected header '{kvp.Key}'.");
                }

                httpRequest.Headers.TryAddWithoutValidation(kvp.Key, resolved.Value);
            }

            return null;
        }

        private async Task<NuntivusServer.Common.Result<string>> ResolveCredentialAsync(
            string? secretRef,
            string? inlineSecret,
            CancellationToken cancellationToken)
        {
            // Unified credential handling:
            // - ref path => resolve through secret resolver
            // - inline path => already accepted for execution scope
            if (!string.IsNullOrWhiteSpace(secretRef))
            {
                return await _secretResolver.ResolveAsync(secretRef, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(inlineSecret))
            {
                return NuntivusServer.Common.Result.Success(inlineSecret);
            }

            return NuntivusServer.Common.Result.Failure<string>(
                NuntivusServer.Common.Error.Validation("Credential is required."));
        }

        private Task<NuntivusServer.Common.Result<string>> ResolveCredentialByKindAsync(
            string kind,
            string value,
            CancellationToken cancellationToken) =>
            kind switch
            {
                "ref" => ResolveCredentialAsync(value, null, cancellationToken),
                "inline" => ResolveCredentialAsync(null, value, cancellationToken),
                _ => Task.FromResult(NuntivusServer.Common.Result.Failure<string>(
                    NuntivusServer.Common.Error.Validation("Unsupported credential kind.")))
            };

        private static string ComputeHmacSignature(string secret, string payload, string timestamp)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
            var hash = hmac.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task<DeliveryExecutionResult?> ValidateDestinationAddressAsync(
            Uri destinationUri,
            CancellationToken cancellationToken)
        {
            IReadOnlyCollection<IPAddress> addresses = Array.Empty<IPAddress>();
            try
            {
                if (IPAddress.TryParse(destinationUri.DnsSafeHost, out var hostIp))
                {
                    addresses = new[] { hostIp };
                }
                else
                {
                    addresses = await Dns.GetHostAddressesAsync(destinationUri.DnsSafeHost, cancellationToken);
                }
            }
            catch (SocketException)
            {
                // DNS may fail transiently; do not misclassify as unsafe.
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var addressValidation = _securityPolicy.ValidateResolvedAddresses(
                addresses,
                SecurityContext,
                _security);
            if (addressValidation.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    addressValidation.Errors[0].Message);
            }

            return null;
        }

        private static async Task<DeliveryExecutionResult> ClassifyResponseAsync(
            HttpResponseMessage response,
            DeliveryExecutionRequest request,
            TimeSpan destinationLatency,
            int maxResponseBodyBytes,
            CancellationToken cancellationToken)
        {
            var statusCode = (int)response.StatusCode;
            var meta = new System.Collections.Generic.Dictionary<string, string>
            {
                ["httpStatus"] = statusCode.ToString(),
                ["destinationLatencyMs"] = destinationLatency.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
            };

            if (response.IsSuccessStatusCode)
            {
                return DeliveryExecutionResult.Success(meta);
            }

            var body = await ReadBoundedBodyAsync(response, maxResponseBodyBytes, cancellationToken);
            if (!string.IsNullOrEmpty(body))
            {
                meta["responseBody"] = body;
            }

            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } d
                    ? d - DateTimeOffset.UtcNow
                    : (TimeSpan?)null);

            var error = $"HTTP {statusCode} {response.ReasonPhrase}".Trim();
            if (!string.IsNullOrEmpty(body))
            {
                error = $"{error} :: {body}";
            }

            return response.StatusCode switch
            {
                HttpStatusCode.RequestTimeout => DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Timeout, error, retryAfter, meta),
                HttpStatusCode.TooManyRequests => DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Throttled, error, retryAfter, meta),
                (HttpStatusCode)425 /* Too Early */ => DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient, error, retryAfter, meta),
                _ when statusCode >= 500 => DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient, error, retryAfter, meta),
                _ when statusCode >= 400 => DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Permanent, error, retryAfter, meta),
                _ => DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient, error, retryAfter, meta),
            };
        }

        private static async Task<string?> ReadBoundedBodyAsync(
            HttpResponseMessage response,
            int maxResponseBodyBytes,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var boundedSize = Math.Max(1, maxResponseBodyBytes);
                var buffer = new byte[boundedSize];
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = await stream.ReadAsync(
                        buffer.AsMemory(read, buffer.Length - read),
                        cancellationToken);
                    if (n == 0) break;
                    read += n;
                }
                if (read == 0) return null;
                var boundedBody = Encoding.UTF8.GetString(buffer, 0, read);
                if (read == buffer.Length)
                    boundedBody += "...[truncated]";

                return boundedBody;
            }
            catch
            {
                return null;
            }
        }
    }
}

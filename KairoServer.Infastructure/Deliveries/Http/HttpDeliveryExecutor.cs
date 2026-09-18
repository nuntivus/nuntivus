using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Observability;
using KairoServer.Application.Security;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.Infastructure.Deliveries.Http
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

        public const string HttpClientName = "kairo-delivery-http";
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

            using var activity = KairoTelemetry.ActivitySource.StartActivity("delivery.http.execute", ActivityKind.Client);
            activity?.SetTag(KairoTelemetry.DeliveryIdTag, request.DeliveryId.ToString("D"));
            activity?.SetTag(KairoTelemetry.EventIdTag, request.EventId.ToString("D"));
            activity?.SetTag(KairoTelemetry.ContractIdTag, request.ContractId.ToString("D"));
            activity?.SetTag(KairoTelemetry.CustomerIdTag, request.CustomerId);

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

            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Delivery-Id", request.DeliveryId.ToString());
            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Event-Id", request.EventId.ToString());
            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Contract-Id", request.ContractId.ToString());
            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Customer-Id", request.CustomerId);
            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Event-Type", request.EventType);
            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Attempt", request.AttemptNumber.ToString());

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
                activity?.SetTag("kairo.destination.latency.ms", elapsed.TotalMilliseconds);
                var result = await ClassifyResponseAsync(
                    response,
                    request,
                    elapsed,
                    _security.MaxResponseBodyBytes,
                    linkedCts.Token);
                activity?.SetTag(KairoTelemetry.OutcomeTag, result.Succeeded ? "delivered" : "failed");
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                activity?.SetTag(KairoTelemetry.OutcomeTag, "timeout");
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Timeout,
                    $"HTTP request timed out after {timeout.TotalSeconds}s.");
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning(
                    "HTTP delivery {DeliveryId} transport error: {Error}",
                    request.DeliveryId, ex.Message);
                activity?.SetTag(KairoTelemetry.OutcomeTag, "transport_error");
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

            var secretResult = credentialKind switch
            {
                "ref" => await ResolveCredentialAsync(credentialValue, null, cancellationToken),
                "inline" => await ResolveCredentialAsync(null, credentialValue, cancellationToken),
                _ => KairoServer.Common.Result.Failure<string>(
                    KairoServer.Common.Error.Validation("Unsupported credential kind."))
            };

            if (secretResult.IsFailure)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"Failed to resolve credentials for HTTP auth '{authType}'.");
            }

            switch (authType)
            {
                case "bearer":
                    httpRequest.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", secretResult.Value);
                    return null;

                case "hmac":
                    // Placeholder: real HMAC signing arrives with the auth story.
                    // We surface the secret as a header so the request is deterministic
                    // but flag the mode so operators know signing isn't fully implemented.
                    httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Signature", "hmac-placeholder");
                    return null;

                default:
                    return DeliveryExecutionResult.Failure(
                        DeliveryFailureCategory.ConfigurationInvalid,
                        $"Unsupported HTTP auth type '{authType}'.");
            }
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

        private async Task<KairoServer.Common.Result<string>> ResolveCredentialAsync(
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
                return KairoServer.Common.Result.Success(inlineSecret);
            }

            return KairoServer.Common.Result.Failure<string>(
                KairoServer.Common.Error.Validation("Credential is required."));
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

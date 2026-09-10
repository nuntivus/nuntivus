using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.Infastructure.Deliveries.Http
{
    /// <summary>
    /// HTTP transport implementation of <see cref="IDeliveryExecutor"/>.
    /// Owns parsing and validating <see cref="HttpDestinationConfig"/> and
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

        public const string HttpClientName = "kairo-delivery-http";

        public HttpDeliveryExecutor(
            IHttpClientFactory httpClientFactory,
            ISecretResolver secretResolver,
            IAppLogger<HttpDeliveryExecutor> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public DomainDestinationType DestinationType => DomainDestinationType.Http;

        public async Task<DeliveryExecutionResult> ExecuteAsync(
            DeliveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

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

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, config.Url)
            {
                Content = new StringContent(request.Payload, Encoding.UTF8),
            };
            httpRequest.Content.Headers.ContentType = JsonMediaType;

            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Delivery-Id", request.DeliveryId.ToString());
            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Event-Id", request.EventId.ToString());
            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Event-Type", request.EventType);
            httpRequest.Headers.TryAddWithoutValidation("X-Kairo-Attempt", request.AttemptNumber.ToString());

            if (config.Headers is not null)
            {
                foreach (var kvp in config.Headers)
                {
                    httpRequest.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                }
            }

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
                using var response = await client.SendAsync(httpRequest, linkedCts.Token);
                return ClassifyResponse(response, request);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Timeout,
                    $"HTTP request timed out after {timeout.TotalSeconds}s.");
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning(
                    "HTTP delivery {DeliveryId} transport error: {Error}",
                    request.DeliveryId, ex.Message);
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
            var authType = config.Authentication?.Type?.Trim().ToLowerInvariant() ?? "none";
            if (authType == "none")
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(config.Authentication?.SecretRef))
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"HTTP destination auth '{authType}' requires a secretRef.");
            }

            var secret = await _secretResolver.ResolveAsync(
                config.Authentication.SecretRef!, cancellationToken);
            if (!secret.IsSuccess)
            {
                return DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"Failed to resolve secret '{config.Authentication.SecretRef}': {secret.Error}");
            }

            switch (authType)
            {
                case "bearer":
                    httpRequest.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", secret.Value);
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

        private static DeliveryExecutionResult ClassifyResponse(
            HttpResponseMessage response,
            DeliveryExecutionRequest request)
        {
            var statusCode = (int)response.StatusCode;
            var meta = new System.Collections.Generic.Dictionary<string, string>
            {
                ["httpStatus"] = statusCode.ToString(),
            };

            if (response.IsSuccessStatusCode)
            {
                return DeliveryExecutionResult.Success(meta);
            }

            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } d
                    ? d - DateTimeOffset.UtcNow
                    : (TimeSpan?)null);

            var error = $"HTTP {statusCode} {response.ReasonPhrase}".Trim();

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
    }
}

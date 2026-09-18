using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using KairoServer.Application.Security;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.Application.Contracts.Destinations
{
    /// <summary>
    /// Handler for <see cref="HttpDestinationConfig"/>: validates the URL, authentication,
    /// timeout and secret reference, then serializes a canonical payload.
    /// </summary>
    public sealed class HttpDestinationConfigHandler : IDestinationConfigHandler
    {
        public const int MinTimeoutSeconds = 1;
        public const int MaxTimeoutSeconds = 300;
        public const int MaxCredentialValueLength = 4096;
        public const string SecretRefScheme = "secret";
        public const string ProtectedRefScheme = "protected";

        public static readonly System.Collections.Generic.HashSet<string> SupportedAuthenticationTypes =
            new(StringComparer.OrdinalIgnoreCase) { "none", "hmac", "bearer" };

        private readonly IDestinationCredentialProtector _protector;
        private readonly HttpDestinationSecurityOption _security;
        private readonly IEndpointSecurityPolicy _securityPolicy;

        public HttpDestinationConfigHandler(
            IDestinationCredentialProtector protector,
            HttpDestinationSecurityOption? security = null,
            IEndpointSecurityPolicy? securityPolicy = null)
        {
            _protector = protector ?? throw new ArgumentNullException(nameof(protector));
            _security = security ?? new HttpDestinationSecurityOption();
            _securityPolicy = securityPolicy ?? new EndpointSecurityPolicy();
        }

        public Type ConfigType => typeof(HttpDestinationConfig);
        public DomainDestinationType DestinationType => DomainDestinationType.Http;
        public EndpointSecurityContext SecurityContext =>
            new(EndpointDirection.Destination, "http", "Destination");

        public Result<Destination> Build(DestinationConfig configuration)
        {
            if (configuration is not HttpDestinationConfig http)
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Expected HTTP destination configuration, got '{configuration?.GetType().Name ?? "null"}'."));
            }

            if (string.IsNullOrWhiteSpace(http.Url))
                return Result.Failure<Destination>(Error.Validation("Destination url is required."));

            if (!Uri.TryCreate(http.Url, UriKind.Absolute, out var destinationUri))
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination url must be an absolute URL."));
            }

            var destinationValidation = _securityPolicy.ValidateEndpointUri(
                destinationUri,
                SecurityContext,
                _security);
            if (destinationValidation.IsFailure)
            {
                return Result<Destination>.Fail(destinationValidation.Errors);
            }

            if (http.TimeoutSeconds is int t
                && (t < MinTimeoutSeconds || t > MaxTimeoutSeconds))
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Destination timeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}."));
            }

            if (http.Authentication is null || string.IsNullOrWhiteSpace(http.Authentication.Type))
                return Result.Failure<Destination>(Error.Validation("Destination authentication is required."));

            var authType = http.Authentication.Type.Trim().ToLowerInvariant();
            if (!SupportedAuthenticationTypes.Contains(authType))
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination authentication.type must be one of: none, hmac, bearer."));
            }

            var headersResult = NormalizePlainHeaders(http.Headers, "Destination headers");
            if (headersResult.IsFailure)
                return Result<Destination>.Fail(headersResult.Errors);

            var secretHeadersResult = NormalizeSecretHeaders(http.SecretHeaders);
            if (secretHeadersResult.IsFailure)
                return Result<Destination>.Fail(secretHeadersResult.Errors);

            var protectedHeadersResult = NormalizeProtectedHeaders(http.ProtectedHeaders);
            if (protectedHeadersResult.IsFailure)
                return Result<Destination>.Fail(protectedHeadersResult.Errors);

            if (headersResult.Value.Keys.Intersect(secretHeadersResult.Value.Keys, StringComparer.OrdinalIgnoreCase).Any())
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination headers and secretHeaders cannot define the same header name."));
            }

            if (headersResult.Value.Keys.Intersect(protectedHeadersResult.Value.Keys, StringComparer.OrdinalIgnoreCase).Any())
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination headers and protectedHeaders cannot define the same header name."));
            }

            var authResult = NormalizeAuthentication(authType, http.Authentication);
            if (authResult.IsFailure)
                return Result<Destination>.Fail(authResult.Errors);

            var protectedHeaders = new Dictionary<string, string>(protectedHeadersResult.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in secretHeadersResult.Value)
            {
                var protectedValue = _protector.Protect(kvp.Value);
                if (protectedValue.IsFailure)
                    return Result<Destination>.Fail(protectedValue.Errors);

                protectedHeaders[kvp.Key] = $"{ProtectedRefScheme}.{protectedValue.Value}";
            }

            var normalized = new HttpDestinationConfig(
                http.Url.Trim(),
                authResult.Value,
                headersResult.Value,
                SecretHeaders: null,
                ProtectedHeaders: protectedHeaders.Count == 0 ? null : protectedHeaders,
                http.TimeoutSeconds);

            var json = JsonSerializer.Serialize(normalized);
            return Result.Success(new Destination(DomainDestinationType.Http, json));
        }

        public Result<string> Redact(string configurationJson)
        {
            if (string.IsNullOrWhiteSpace(configurationJson))
                return Result.Failure<string>(Error.Validation("Destination configuration is required."));

            HttpDestinationConfig? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<HttpDestinationConfig>(configurationJson);
            }
            catch (JsonException ex)
            {
                return Result.Failure<string>(Error.Validation(
                    $"Destination configuration is not valid JSON: {ex.GetType().Name}."));
            }

            if (parsed is null)
                return Result.Failure<string>(Error.Validation("Destination configuration is empty."));

            var redactedSecretHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (parsed.SecretHeaders is not null)
            {
                foreach (var header in parsed.SecretHeaders.Keys)
                    redactedSecretHeaders[header] = "***";
            }

            if (parsed.ProtectedHeaders is not null)
            {
                foreach (var header in parsed.ProtectedHeaders.Keys)
                    redactedSecretHeaders[header] = "***";
            }

            var redactedAuth = parsed.Authentication is null
                ? new HttpAuthentication("none")
                : new HttpAuthentication(
                    parsed.Authentication.Type,
                    parsed.Authentication.Credential is null
                        ? null
                        : new HttpCredential(parsed.Authentication.Credential.Kind, "***"));

            var redacted = new HttpDestinationConfig(
                parsed.Url,
                redactedAuth,
                parsed.Headers,
                SecretHeaders: redactedSecretHeaders.Count == 0 ? null : redactedSecretHeaders,
                ProtectedHeaders: null,
                parsed.TimeoutSeconds);

            return Result.Success(JsonSerializer.Serialize(redacted));
        }

        private Result<HttpAuthentication> NormalizeAuthentication(string authType, HttpAuthentication authentication)
        {
            var credential = authentication.Credential;
            var credentialKind = credential?.Kind?.Trim().ToLowerInvariant();
            var credentialValue = credential?.Value?.Trim();

            if (authType == "none")
            {
                if (credential is not null)
                {
                    return Result.Failure<HttpAuthentication>(Error.Validation(
                        "Destination authentication.credential must be empty when type is 'none'."));
                }

                return Result.Success(new HttpAuthentication(authType));
            }

            if (credential is null)
            {
                return Result.Failure<HttpAuthentication>(Error.Validation(
                    $"HTTP destination auth '{authType}' requires authentication.credential."));
            }

            if (string.IsNullOrWhiteSpace(credentialKind)
                || string.IsNullOrWhiteSpace(credentialValue))
            {
                return Result.Failure<HttpAuthentication>(Error.Validation(
                    "Destination authentication.credential.kind and authentication.credential.value are required."));
            }

            if (credentialValue.Length > MaxCredentialValueLength)
            {
                return Result.Failure<HttpAuthentication>(Error.Validation(
                    $"Destination authentication.credential.value exceeds maximum length of {MaxCredentialValueLength}."));
            }

            if (credentialKind == "ref")
            {
                if (!ConfigReference.TryParse(credentialValue, SecretRefScheme, out _)
                    && !ConfigReference.TryParse(credentialValue, ProtectedRefScheme, out _))
                {
                    return Result.Failure<HttpAuthentication>(Error.Validation(
                        "Destination authentication.credential.value must be a reference like 'secret.<path>' or 'protected.<payload>' when kind is 'ref'."));
                }

                return Result.Success(new HttpAuthentication(authType, new HttpCredential("ref", credentialValue!)));
            }

            if (credentialKind == "inline")
            {
                var protectedSecret = _protector.Protect(credentialValue!);
                if (protectedSecret.IsFailure)
                    return Result<HttpAuthentication>.Fail(protectedSecret.Errors);

                return Result.Success(new HttpAuthentication(
                    authType,
                    new HttpCredential("ref", $"{ProtectedRefScheme}.{protectedSecret.Value}")));
            }

            return Result.Failure<HttpAuthentication>(Error.Validation(
                "Destination authentication.credential.kind must be either 'ref' or 'inline'."));
        }

        private static Result<IReadOnlyDictionary<string, string>> NormalizePlainHeaders(
            IReadOnlyDictionary<string, string>? headers,
            string label)
        {
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers is null)
                return Result.Success<IReadOnlyDictionary<string, string>>(normalized);

            foreach (var kvp in headers)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                    return Result.Failure<IReadOnlyDictionary<string, string>>(Error.Validation($"{label} cannot contain empty header names."));

                var name = kvp.Key.Trim();
                var value = kvp.Value?.Trim() ?? string.Empty;
                normalized[name] = value;
            }

            return Result.Success<IReadOnlyDictionary<string, string>>(normalized);
        }

        private static Result<IReadOnlyDictionary<string, string>> NormalizeSecretHeaders(
            IReadOnlyDictionary<string, string>? headers)
        {
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers is null)
                return Result.Success<IReadOnlyDictionary<string, string>>(normalized);

            foreach (var kvp in headers)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    return Result.Failure<IReadOnlyDictionary<string, string>>(Error.Validation(
                        "Destination secretHeaders cannot contain empty header names."));
                }

                if (string.IsNullOrWhiteSpace(kvp.Value))
                {
                    return Result.Failure<IReadOnlyDictionary<string, string>>(Error.Validation(
                        $"Destination secretHeaders value for '{kvp.Key}' cannot be empty."));
                }

                normalized[kvp.Key.Trim()] = kvp.Value.Trim();
            }

            return Result.Success<IReadOnlyDictionary<string, string>>(normalized);
        }

        private static Result<IReadOnlyDictionary<string, string>> NormalizeProtectedHeaders(
            IReadOnlyDictionary<string, string>? protectedHeaders)
        {
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (protectedHeaders is null)
                return Result.Success<IReadOnlyDictionary<string, string>>(normalized);

            foreach (var kvp in protectedHeaders)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    return Result.Failure<IReadOnlyDictionary<string, string>>(Error.Validation(
                        "Destination protectedHeaders cannot contain empty header names."));
                }

                if (!ConfigReference.TryParse(kvp.Value, ProtectedRefScheme, out _)
                    && !ConfigReference.TryParse(kvp.Value, SecretRefScheme, out _))
                {
                    return Result.Failure<IReadOnlyDictionary<string, string>>(Error.Validation(
                        $"Destination protectedHeaders value for '{kvp.Key}' must be a reference like 'protected.<payload>' or 'secret.<path>'."));
                }

                normalized[kvp.Key.Trim()] = kvp.Value.Trim();
            }

            return Result.Success<IReadOnlyDictionary<string, string>>(normalized);
        }
    }
}

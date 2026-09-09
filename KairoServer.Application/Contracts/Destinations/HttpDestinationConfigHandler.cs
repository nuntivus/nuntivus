using System;
using System.Text.Json;
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
        public const string SecretRefScheme = "secret";

        public static readonly System.Collections.Generic.HashSet<string> SupportedAuthenticationTypes =
            new(StringComparer.OrdinalIgnoreCase) { "none", "hmac", "bearer" };

        public Type ConfigType => typeof(HttpDestinationConfig);

        public Result<Destination> Build(DestinationConfig configuration)
        {
            if (configuration is not HttpDestinationConfig http)
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Expected HTTP destination configuration, got '{configuration?.GetType().Name ?? "null"}'."));
            }

            if (string.IsNullOrWhiteSpace(http.Url))
                return Result.Failure<Destination>(Error.Validation("Destination url is required."));

            if (!Uri.TryCreate(http.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination url must be an absolute http or https URL."));
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

            if (authType == "none")
            {
                if (!string.IsNullOrWhiteSpace(http.Authentication.SecretRef))
                {
                    return Result.Failure<Destination>(Error.Validation(
                        "Destination authentication.secretRef must be empty when type is 'none'."));
                }
            }
            else
            {
                if (!ConfigReference.TryParse(http.Authentication.SecretRef, SecretRefScheme, out _))
                {
                    return Result.Failure<Destination>(Error.Validation(
                        "Destination authentication.secretRef must be a reference like 'secret.<path>'."));
                }
            }

            var normalized = new HttpDestinationConfig(
                http.Url.Trim(),
                new HttpAuthentication(authType, string.IsNullOrWhiteSpace(http.Authentication.SecretRef)
                    ? null
                    : http.Authentication.SecretRef!.Trim()),
                http.Headers,
                http.TimeoutSeconds);

            var json = JsonSerializer.Serialize(normalized);
            return Result.Success(new Destination(DomainDestinationType.Http, json));
        }
    }
}

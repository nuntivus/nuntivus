using System.Text.Json;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Sources
{
    public sealed record HttpSourceAuthentication(string Type, string? SecretRef = null);
    public sealed record HttpSourceConfiguration(HttpSourceAuthentication Authentication, string? Method = null);

    public static class HttpSourceConfigurationValidator
    {
        public static Result<string> Normalize(string configuration)
        {
            try
            {
                if (configuration.Length > 4096)
                    return Result.Failure<string>(Error.Validation("Source configuration is too large."));

                using var document = JsonDocument.Parse(configuration);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => p.Name is not ("authentication" or "method"))
                    || !root.TryGetProperty("authentication", out var auth) || auth.ValueKind != JsonValueKind.Object
                    || auth.EnumerateObject().Any(p => p.Name != "type" && p.Name != "secretRef")
                    || !auth.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                    return Result.Failure<string>(Error.Validation("HTTP Source requires an authentication object with a type."));

                var method = "POST";
                if (root.TryGetProperty("method", out var selectedMethod))
                {
                    if (selectedMethod.ValueKind != JsonValueKind.String)
                        return Result.Failure<string>(Error.Validation("HTTP Source method must be POST, PUT, or PATCH."));
                    method = selectedMethod.GetString()!.Trim().ToUpperInvariant();
                }
                if (method is not ("POST" or "PUT" or "PATCH"))
                    return Result.Failure<string>(Error.Validation("HTTP Source method must be POST, PUT, or PATCH."));

                var mode = type.GetString();
                if (mode == "none" && auth.EnumerateObject().Count() == 1)
                    return Result.Success(JsonSerializer.Serialize(new { authentication = new { type = "none" }, method }));

                if (mode == "token" && auth.EnumerateObject().Count() == 2
                    && auth.TryGetProperty("secretRef", out var secret) && secret.ValueKind == JsonValueKind.String
                    && ConfigReference.TryParse(secret.GetString(), "secret", out var reference))
                {
                    return Result.Success(JsonSerializer.Serialize(new
                    {
                        authentication = new { type = "token", secretRef = reference.ToString() }, method
                    }));
                }

                return Result.Failure<string>(Error.Validation("HTTP Source authentication must be 'none' or 'token' with a managed secretRef."));
            }
            catch (JsonException)
            {
                return Result.Failure<string>(Error.Validation("Source configuration must be valid JSON."));
            }
        }
    }
}

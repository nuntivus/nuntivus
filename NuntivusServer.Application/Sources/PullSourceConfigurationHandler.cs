using System.Text.Json;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed class PullSourceConfigurationHandler : ISourceConfigurationHandler
    {
        public PullSourceConfigurationHandler(SourceType type) => Type = type;
        public SourceType Type { get; }

        public Result<string> Normalize(string configuration)
        {
            try
            {
                if (configuration.Length > 8192) return Result.Failure<string>(Error.Validation("Source configuration exceeds 8192 characters."));
                using var document = JsonDocument.Parse(configuration);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return Result.Failure<string>(Error.Validation("Source configuration must be an object."));
                var required = Type switch
                {
                    SourceType.RABBITMQ => new[] { "uri", "queue" },
                    SourceType.KAFKA => new[] { "bootstrapServers", "topic", "groupId" },
                    SourceType.SQS => new[] { "region", "queueUrl" },
                    SourceType.NATS => new[] { "url", "stream", "subject", "durable" },
                    SourceType.HTTP_POLLING => new[] { "url" },
                    SourceType.FOLDER => new[] { "path" },
                    SourceType.S3 => new[] { "region", "bucket", "prefix" },
                    _ => Array.Empty<string>()
                };
                if (required.Length == 0 || required.Any(key => !root.TryGetProperty(key, out var value)
                        || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())))
                    return Result.Failure<string>(Error.Validation($"{Type} configuration is missing required fields."));
                var allowed = required.Concat(Type switch
                {
                    SourceType.RABBITMQ or SourceType.KAFKA => new[] { "secretRef", "username" },
                    SourceType.NATS => new[] { "secretRef" },
                    SourceType.SQS or SourceType.S3 => new[] { "secretRef", "accessKeyId", "intervalSeconds" },
                    SourceType.HTTP_POLLING => new[] { "secretRef", "intervalSeconds", "method", "headers", "body" },
                    _ => new[] { "intervalSeconds" }
                }).ToHashSet();
                if (root.EnumerateObject().Any(field => !allowed.Contains(field.Name)
                    || (field.Name is not ("intervalSeconds" or "headers" or "body") && field.Value.ValueKind != JsonValueKind.String)))
                    return Result.Failure<string>(Error.Validation("Source configuration contains unsupported fields."));
                if (Type == SourceType.HTTP_POLLING)
                {
                    var method = root.TryGetProperty("method", out var selectedMethod)
                        ? selectedMethod.ValueKind == JsonValueKind.String ? selectedMethod.GetString()?.Trim().ToUpperInvariant() : null
                        : "GET";
                    if (method is not ("GET" or "POST"))
                        return Result.Failure<string>(Error.Validation("HTTP polling method must be GET or POST."));
                    if (root.TryGetProperty("body", out var body)
                        && (method != "POST" || body.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)
                            || body.GetRawText().Length > 4096))
                        return Result.Failure<string>(Error.Validation("HTTP polling body must be a JSON object or array (at most 4096 characters) and requires POST."));
                    if (root.TryGetProperty("headers", out var headers))
                    {
                        if (headers.ValueKind != JsonValueKind.Object || headers.EnumerateObject().Count() > 20)
                            return Result.Failure<string>(Error.Validation("HTTP polling headers must be an object with at most 20 entries."));
                        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var header in headers.EnumerateObject())
                        {
                            if (!names.Add(header.Name) || header.Name.Length is < 1 or > 64
                                || !System.Text.RegularExpressions.Regex.IsMatch(header.Name, "^[A-Za-z0-9][A-Za-z0-9-]*$")
                                || header.Name is { } name && (name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                                || header.Value.ValueKind != JsonValueKind.String || header.Value.GetString() is not { Length: <= 1024 } value
                                || value.Any(char.IsControl))
                                return Result.Failure<string>(Error.Validation("HTTP polling contains an unsafe header name or value."));
                        }
                    }
                }
                if (root.TryGetProperty("secretRef", out var secret)
                    && !ConfigReference.TryParse(secret.GetString(), "secret", out _))
                    return Result.Failure<string>(Error.Validation("secretRef must reference a managed secret."));
                if (root.TryGetProperty("accessKeyId", out _) != root.TryGetProperty("secretRef", out _)
                    && Type is SourceType.SQS or SourceType.S3)
                    return Result.Failure<string>(Error.Validation("AWS accessKeyId and secretRef must be supplied together or omitted for role credentials."));
                if (Type == SourceType.KAFKA && root.TryGetProperty("secretRef", out _)
                    != root.TryGetProperty("username", out _))
                    return Result.Failure<string>(Error.Validation("Kafka username and secretRef must be supplied together for SASL or omitted for TLS."));
                if (root.TryGetProperty("intervalSeconds", out var interval) &&
                    (interval.ValueKind != JsonValueKind.Number || !interval.TryGetInt32(out var seconds) || seconds is < 10 or > 3600))
                    return Result.Failure<string>(Error.Validation("intervalSeconds must be between 10 and 3600."));
                if (Type is SourceType.RABBITMQ or SourceType.NATS or SourceType.HTTP_POLLING)
                {
                    var endpoint = root.GetProperty(Type == SourceType.RABBITMQ ? "uri" : "url").GetString()!;
                    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
                        || (Type == SourceType.HTTP_POLLING && uri.Scheme != Uri.UriSchemeHttps)
                        || (Type == SourceType.RABBITMQ && uri.Scheme != "amqps")
                        || (Type == SourceType.NATS && uri.Scheme != "tls"))
                        return Result.Failure<string>(Error.Validation("A secure endpoint without inline credentials is required."));
                }
                if (Type == SourceType.FOLDER && !Path.IsPathFullyQualified(root.GetProperty("path").GetString()!))
                    return Result.Failure<string>(Error.Validation("Folder path must be absolute."));
                return Result.Success(root.GetRawText());
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                return Result.Failure<string>(Error.Validation("Source configuration must be valid JSON."));
            }
        }
    }
}

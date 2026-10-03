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
                    SourceType.HTTP_POLLING => new[] { "secretRef", "intervalSeconds" },
                    _ => new[] { "intervalSeconds" }
                }).ToHashSet();
                if (root.EnumerateObject().Any(field => !allowed.Contains(field.Name)
                    || (field.Name != "intervalSeconds" && field.Value.ValueKind != JsonValueKind.String)))
                    return Result.Failure<string>(Error.Validation("Source configuration contains unsupported fields."));
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

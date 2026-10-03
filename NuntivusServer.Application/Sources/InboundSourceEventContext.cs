using System.Text.Json;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Sources
{
    public sealed record InboundSourceEventContext(string SourceId, string Body, string? StableKey);

    public static class InboundSourceEnvelope
    {
        public static Result<CanonicalSourceEvent> Parse(InboundSourceEventContext context)
        {
            try
            {
                if (System.Text.Encoding.UTF8.GetByteCount(context.Body) > 1024 * 1024)
                    return Result.Failure<CanonicalSourceEvent>(Error.Validation("Inbound event exceeds 1 MiB."));
                using var document = JsonDocument.Parse(context.Body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(type.GetString())
                    || !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                    return Result.Failure<CanonicalSourceEvent>(Error.Validation("Inbound envelope requires type and object payload."));
                var key = root.TryGetProperty("idempotencyKey", out var keyField) && keyField.ValueKind == JsonValueKind.String
                    ? keyField.GetString() : context.StableKey;
                if (keyField.ValueKind != JsonValueKind.Undefined && keyField.ValueKind != JsonValueKind.String)
                    return Result.Failure<CanonicalSourceEvent>(Error.Validation("idempotencyKey must be a string."));
                if (string.IsNullOrWhiteSpace(key))
                    return Result.Failure<CanonicalSourceEvent>(Error.Validation("Inbound envelope requires a stable idempotency key."));
                var timestamp = DateTimeOffset.UtcNow;
                if (root.TryGetProperty("timestamp", out var timeField)
                    && (timeField.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(timeField.GetString(), out timestamp)))
                    return Result.Failure<CanonicalSourceEvent>(Error.Validation("timestamp must be a valid date-time."));
                string? metadata = null;
                if (root.TryGetProperty("metadata", out var metadataField))
                {
                    if (metadataField.ValueKind != JsonValueKind.Object || metadataField.EnumerateObject().Any(field =>
                            field.Name is not ("traceId" or "correlationId") || field.Value.ValueKind != JsonValueKind.String))
                        return Result.Failure<CanonicalSourceEvent>(Error.Validation("metadata only supports string traceId and correlationId."));
                    metadata = metadataField.GetRawText();
                }
                return Result.Success(new CanonicalSourceEvent(key.Trim(), type.GetString()!.Trim(),
                    NuntivusServer.Domain.Sources.SourceId.Create(context.SourceId), timestamp,
                    payload.GetRawText(), metadata));
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                return Result.Failure<CanonicalSourceEvent>(Error.Validation("Inbound envelope is invalid JSON or Source ID."));
            }
        }
    }
}

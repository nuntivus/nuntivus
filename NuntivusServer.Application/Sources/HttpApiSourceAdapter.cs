using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Security;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed class HttpApiSourceAdapter : ISourceAdapter
    {
        public SourceType Type => SourceType.HTTP_API;

        public EndpointSecurityContext SecurityContext =>
            new(EndpointDirection.Source, "http", "events.publish");

        public Task<Result<CanonicalSourceEvent>> NormalizeAsync(object adapterContext, CancellationToken cancellationToken)
        {
            if (adapterContext is not HttpApiSourceEventContext request)
            {
                return Task.FromResult(Result.Failure<CanonicalSourceEvent>(
                    Error.Validation("HTTP source adapter context is invalid.")));
            }

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                return Task.FromResult(Result.Failure<CanonicalSourceEvent>(Error.Validation("idempotencyKey is required.")));

            if (string.IsNullOrWhiteSpace(request.Type))
                return Task.FromResult(Result.Failure<CanonicalSourceEvent>(Error.Validation("type is required.")));

            try
            {
                var sourceId = SourceId.Create(request.Source);

                if (!Guid.TryParse(sourceId.Value, out _))
                {
                    return Task.FromResult(Result.Failure<CanonicalSourceEvent>(
                        Error.Validation("source must be a valid GUID.")));
                }

                var metadata = NormalizeMetadata(request.Metadata);
                if (metadata.IsFailure)
                    return Task.FromResult(Result<CanonicalSourceEvent>.Fail(metadata.Errors));

                var canonical = new CanonicalSourceEvent(
                    request.IdempotencyKey.Trim(),
                    request.Type.Trim(),
                    sourceId,
                    request.Timestamp,
                    NormalizeJsonObject(request.Payload),
                    metadata.Value);

                return Task.FromResult(Result.Success(canonical));
            }
            catch (ArgumentException ex)
            {
                return Task.FromResult(Result.Failure<CanonicalSourceEvent>(Error.Validation(ex.Message)));
            }
        }

        private static string NormalizeJsonObject(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "{}";

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    ? doc.RootElement.GetRawText()
                    : "{}";
            }
            catch
            {
                return "{}";
            }
        }

        private static Result<string> NormalizeMetadata(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Result.Success("{}");
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return Result.Failure<string>(Error.Validation("metadata must be an object."));

                var metadata = new System.Collections.Generic.Dictionary<string, string>();
                foreach (var field in document.RootElement.EnumerateObject())
                {
                    if (field.Name is not ("traceId" or "correlationId") || field.Value.ValueKind != JsonValueKind.String)
                        return Result.Failure<string>(Error.Validation("metadata only supports string traceId and correlationId fields."));
                    metadata[field.Name] = field.Value.GetString() ?? string.Empty;
                }
                return Result.Success(JsonSerializer.Serialize(metadata));
            }
            catch (JsonException)
            {
                return Result.Failure<string>(Error.Validation("metadata must be a valid JSON object."));
            }
        }
    }
}

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Deliveries.Execution
{
    public sealed class DeliveryPayloadTransformer : IDeliveryPayloadTransformer
    {
        private const int SupportedVersion = 1;

        public Result<string> Apply(string payload, string? transformationConfiguration)
        {
            if (string.IsNullOrWhiteSpace(transformationConfiguration))
                return Result.Success(payload);

            ContractPayloadTransformation? transformation;
            try
            {
                transformation = JsonSerializer.Deserialize<ContractPayloadTransformation>(transformationConfiguration);
            }
            catch (JsonException ex)
            {
                return Result.Failure<string>(Error.Validation($"Transformation configuration is invalid JSON. {ex.Message}"));
            }

            if (transformation is null)
                return Result.Failure<string>(Error.Validation("Transformation configuration could not be parsed."));

            if (transformation.Version != SupportedVersion)
                return Result.Failure<string>(Error.Validation($"Unsupported transformation version '{transformation.Version}'."));

            JsonDocument payloadDocument;
            try
            {
                payloadDocument = JsonDocument.Parse(payload);
            }
            catch (JsonException ex)
            {
                return Result.Failure<string>(Error.Validation($"Delivery payload is invalid JSON. {ex.Message}"));
            }

            using (payloadDocument)
            {
                var transformed = new JsonObject();
                foreach (var mapping in transformation.FieldMappings)
                {
                    if (!TryResolve(payloadDocument.RootElement, mapping.SourceSelector, out var sourceValue))
                    {
                        return Result.Failure<string>(Error.Validation(
                            $"Transformation source selector '{mapping.SourceSelector}' was not found in payload."));
                    }

                    if (!TrySet(transformed, mapping.TargetSelector, JsonNode.Parse(sourceValue.GetRawText())))
                    {
                        return Result.Failure<string>(Error.Validation(
                            $"Transformation target selector '{mapping.TargetSelector}' is invalid or conflicts with existing values."));
                    }
                }

                return Result.Success(transformed.ToJsonString());
            }
        }

        private static bool TryResolve(JsonElement root, string selector, out JsonElement value)
        {
            value = root;
            var current = root;
            foreach (var segment in selector.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                    return false;
            }

            value = current;
            return true;
        }

        private static bool TrySet(JsonObject root, string selector, JsonNode? value)
        {
            var segments = selector.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return false;

            JsonObject current = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var segment = segments[i];
                if (current[segment] is null)
                {
                    var child = new JsonObject();
                    current[segment] = child;
                    current = child;
                    continue;
                }

                if (current[segment] is JsonObject existing)
                {
                    current = existing;
                    continue;
                }

                return false;
            }

            var leaf = segments[^1];
            if (current[leaf] is JsonObject)
                return false;

            current[leaf] = value;
            return true;
        }
    }
}

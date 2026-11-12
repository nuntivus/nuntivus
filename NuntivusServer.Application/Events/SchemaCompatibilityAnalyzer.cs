using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using NuntivusServer.Contracts.Events;

namespace NuntivusServer.Application.Events
{
    public sealed class SchemaCompatibilityAnalyzer : ISchemaCompatibilityAnalyzer
    {
        private static readonly HashSet<string> Supported =
        [
            "$schema", "$id", "title", "description", "default", "examples", "deprecated", "readOnly", "writeOnly", "format",
            "type", "enum", "const", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
            "minLength", "maxLength", "pattern", "properties", "required", "additionalProperties", "minProperties", "maxProperties",
            "items", "minItems", "maxItems", "uniqueItems"
        ];
        private static readonly string[] AllTypes = ["null", "boolean", "object", "array", "number", "integer", "string"];

        public SchemaCompatibilityReport Compare(string eventType, int version, int againstVersion,
            string previousSchema, string proposedSchema, SchemaCompatibilityMode mode)
        {
            var previous = JsonNode.Parse(previousSchema)!;
            var proposed = JsonNode.Parse(proposedSchema)!;
            var findings = new List<SchemaCompatibilityFinding>();
            if (mode == SchemaCompatibilityMode.Backward)
                CompareInclusion(previous, proposed, "", findings);
            else
                CompareInclusion(proposed, previous, "", findings);
            var status = findings.Any(f => f.Status == SchemaCompatibilityStatus.Indeterminate)
                ? SchemaCompatibilityStatus.Indeterminate
                : findings.Any(f => f.Status == SchemaCompatibilityStatus.Breaking)
                    ? SchemaCompatibilityStatus.Breaking : SchemaCompatibilityStatus.Compatible;
            return new(eventType, version, againstVersion, mode, status, findings.Take(100).ToArray());
        }

        private static void CompareInclusion(JsonNode source, JsonNode target, string path, List<SchemaCompatibilityFinding> findings)
        {
            if (JsonNode.DeepEquals(source, target) || IsBoolean(source, false) || IsBoolean(target, true)) return;
            if (IsBoolean(target, false))
            {
                Add(findings, path, "false", SchemaCompatibilityStatus.Breaking, "The receiving schema rejects all values.");
                return;
            }
            var from = source as JsonObject ?? new JsonObject();
            var to = target as JsonObject ?? new JsonObject();
            foreach (var keyword in from.Select(p => p.Key).Union(to.Select(p => p.Key)).Where(k => !Supported.Contains(k)))
                Add(findings, path, keyword, SchemaCompatibilityStatus.Indeterminate, "This construct requires review; compatibility cannot be proven by the supported comparison rules.");

            var sourceTypes = Types(from);
            var targetTypes = Types(to);
            if (sourceTypes.Any(type => !targetTypes.Contains(type) && !(type == "integer" && targetTypes.Contains("number"))))
                Add(findings, path, "type", SchemaCompatibilityStatus.Breaking, "The receiving schema narrows the allowed types.");

            if (to.TryGetPropertyValue("const", out var targetConst))
            {
                var values = Values(from);
                if (values is null || values.Any(value => !JsonNode.DeepEquals(value, targetConst)))
                    Add(findings, path, "const", SchemaCompatibilityStatus.Breaking, "The receiving schema restricts values to a different constant.");
            }
            if (to["enum"] is JsonArray targetEnum)
            {
                var values = Values(from);
                if (values is null || values.Any(value => !targetEnum.Any(allowed => JsonNode.DeepEquals(value, allowed))))
                    Add(findings, path, "enum", SchemaCompatibilityStatus.Breaking, "The receiving schema narrows the allowed enum values.");
            }

            if (sourceTypes.Contains("number") || sourceTypes.Contains("integer"))
            {
                CompareNumberBound(from, to, path, true, findings);
                CompareNumberBound(from, to, path, false, findings);
                if (to["multipleOf"] is JsonNode targetMultiple)
                {
                    if (!TryNumber(targetMultiple, out var divisor) || divisor <= 0
                        || (from["multipleOf"] is JsonNode sourceMultiple && !TryNumber(sourceMultiple, out _)))
                        Add(findings, path, "multipleOf", SchemaCompatibilityStatus.Indeterminate, "Numeric precision is outside the supported comparison range.");
                    else if (from["multipleOf"] is not JsonNode multiple || !TryNumber(multiple, out var factor) || factor % divisor != 0)
                        Add(findings, path, "multipleOf", SchemaCompatibilityStatus.Breaking, "The receiving schema restricts numeric multiples.");
                }
            }
            if (sourceTypes.Contains("string"))
            {
                CompareLimit(from, to, path, "minLength", true, findings);
                CompareLimit(from, to, path, "maxLength", false, findings);
                if (to["pattern"] is not null && !JsonNode.DeepEquals(from["pattern"], to["pattern"]))
                    Add(findings, path, "pattern", SchemaCompatibilityStatus.Indeterminate, "Changed regular expressions require review.");
            }
            if (sourceTypes.Contains("object"))
            {
                CompareLimit(from, to, path, "minProperties", true, findings);
                CompareLimit(from, to, path, "maxProperties", false, findings);
                var required = (from["required"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToHashSet() ?? [];
                if (to["required"] is JsonArray targetRequired)
                    foreach (var property in targetRequired.Select(n => n!.GetValue<string>()).Where(p => !required.Contains(p)))
                        Add(findings, path, "required", SchemaCompatibilityStatus.Breaking, $"The receiving schema adds required property '{property}'.");

                var sourceProperties = from["properties"] as JsonObject ?? new JsonObject();
                var targetProperties = to["properties"] as JsonObject ?? new JsonObject();
                foreach (var property in sourceProperties.Select(p => p.Key).Union(targetProperties.Select(p => p.Key)))
                    CompareInclusion(sourceProperties[property] ?? from["additionalProperties"] ?? JsonValue.Create(true)!,
                        targetProperties[property] ?? to["additionalProperties"] ?? JsonValue.Create(true)!,
                        path + "/properties/" + Escape(property), findings);
                CompareInclusion(from["additionalProperties"] ?? JsonValue.Create(true)!, to["additionalProperties"] ?? JsonValue.Create(true)!,
                    path + "/additionalProperties", findings);
            }
            if (sourceTypes.Contains("array"))
            {
                CompareLimit(from, to, path, "minItems", true, findings);
                CompareLimit(from, to, path, "maxItems", false, findings);
                if (IsBoolean(to["uniqueItems"], true) && !IsBoolean(from["uniqueItems"], true))
                    Add(findings, path, "uniqueItems", SchemaCompatibilityStatus.Breaking, "The receiving schema requires unique array items.");
                CompareInclusion(from["items"] ?? JsonValue.Create(true)!, to["items"] ?? JsonValue.Create(true)!, path + "/items", findings);
            }
        }

        private static void CompareLimit(JsonObject from, JsonObject to, string path, string keyword, bool minimum, List<SchemaCompatibilityFinding> findings)
        {
            if (to[keyword] is not JsonNode target) return;
            if (!TryNumber(target, out var limit) || (from[keyword] is JsonNode existing && !TryNumber(existing, out _)))
                Add(findings, path, keyword, SchemaCompatibilityStatus.Indeterminate, "Numeric precision is outside the supported comparison range.");
            else if (from[keyword] is not JsonNode source || !TryNumber(source, out var oldLimit) || (minimum ? limit > oldLimit : limit < oldLimit))
                Add(findings, path, keyword, SchemaCompatibilityStatus.Breaking, "The receiving schema tightens a size constraint.");
        }

        private static void CompareNumberBound(JsonObject from, JsonObject to, string path, bool minimum, List<SchemaCompatibilityFinding> findings)
        {
            var keyword = minimum ? "minimum" : "maximum";
            var exclusive = minimum ? "exclusiveMinimum" : "exclusiveMaximum";
            (decimal Value, bool Exclusive)? Bound(JsonObject obj)
            {
                (decimal Value, bool Exclusive)? bound = null;
                foreach (var key in new[] { keyword, exclusive })
                {
                    if (obj[key] is not JsonNode value) continue;
                    if (!TryNumber(value, out var number))
                    {
                        Add(findings, path, key, SchemaCompatibilityStatus.Indeterminate, "Numeric precision is outside the supported comparison range.");
                        continue;
                    }
                    if (bound is null || (minimum ? number > bound.Value.Value : number < bound.Value.Value)
                        || (number == bound.Value.Value && key == exclusive))
                        bound = (number, key == exclusive);
                }
                return bound;
            }
            var target = Bound(to);
            var source = Bound(from);
            if (target is not null && (source is null
                || (minimum ? target.Value.Value > source.Value.Value : target.Value.Value < source.Value.Value)
                || (target.Value.Value == source.Value.Value && target.Value.Exclusive && !source.Value.Exclusive)))
                Add(findings, path, keyword, SchemaCompatibilityStatus.Breaking, "The receiving schema tightens a numeric bound.");
        }

        private static HashSet<string> Types(JsonObject schema) => schema["type"] switch
        {
            JsonArray types => types.Select(n => n!.GetValue<string>()).ToHashSet(),
            JsonValue value => [value.GetValue<string>()],
            _ => AllTypes.ToHashSet()
        };
        private static IEnumerable<JsonNode?>? Values(JsonObject schema) => schema.ContainsKey("const")
            ? new[] { schema["const"] } : schema["enum"] as JsonArray;
        private static bool IsBoolean(JsonNode? node, bool expected) => node is JsonValue value && value.TryGetValue<bool>(out var result) && result == expected;
        private static bool TryNumber(JsonNode node, out decimal value)
        {
            var raw = node.ToJsonString();
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            var parts = raw.ToLowerInvariant().Split('e');
            var mantissa = parts[0];
            var exponent = 0;
            if (parts.Length > 1 && (!int.TryParse(parts[1], out exponent) || Math.Abs((long)exponent) > 100)) return false;
            var dot = mantissa.IndexOf('.');
            if (dot >= 0) exponent -= mantissa.Length - dot - 1;
            if (mantissa.Length > 100 || Math.Abs((long)exponent) > 100) return false;
            var coefficient = BigInteger.Parse(mantissa.Replace(".", ""), CultureInfo.InvariantCulture);
            var bits = decimal.GetBits(value);
            var scaled = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
            if (bits[3] < 0) scaled = -scaled;
            var shift = exponent + ((bits[3] >> 16) & 0x7f);
            return coefficient * BigInteger.Pow(10, Math.Max(shift, 0)) == scaled * BigInteger.Pow(10, Math.Max(-shift, 0));
        }
        private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
        private static void Add(List<SchemaCompatibilityFinding> findings, string path, string keyword, SchemaCompatibilityStatus status, string message) =>
            findings.Add(new(path + "/" + Escape(keyword), keyword, status, message));
    }
}

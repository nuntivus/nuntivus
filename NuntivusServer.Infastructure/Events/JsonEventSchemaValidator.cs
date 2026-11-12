using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using NuntivusServer.Application.Events;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Events
{
    public sealed class JsonEventSchemaValidator : IEventSchemaValidator
    {
        public const string Dialect = "https://json-schema.org/draft/2020-12/schema";
        private const int MaxSchemaLength = 1_000_000;
        private const int MaxErrors = 100;
        private static readonly HashSet<string> SchemaMaps = ["$defs", "properties", "patternProperties", "dependentSchemas"];
        private static readonly HashSet<string> SchemaArrays = ["allOf", "anyOf", "oneOf", "prefixItems"];
        private static readonly HashSet<string> SchemaValues =
        [
            "additionalProperties", "unevaluatedProperties", "propertyNames", "items", "contains",
            "unevaluatedItems", "not", "if", "then", "else", "contentSchema"
        ];

        public Result ValidateSchema(string schema)
        {
            if (string.IsNullOrWhiteSpace(schema) || schema.Length > MaxSchemaLength)
                return Invalid("Schema is required and must be at most 1,000,000 characters.", "/schema");

            try
            {
                var node = JsonNode.Parse(schema);
                var policy = ValidateSchemaPolicy(node, "/schema", true);
                if (policy.IsFailure)
                    return policy;

                var evaluation = MetaSchemas.Draft202012.Evaluate(node, CreateOptions());
                if (!evaluation.IsValid)
                    return ToResult(evaluation, "/schema");

                var references = ValidateLocalReferences(node);
                if (references.IsFailure)
                    return references;

                _ = JsonSchema.FromText(schema);
                return Result.Success();
            }
            catch (Exception ex) when (IsSchemaException(ex))
            {
                return Invalid("Schema must be a valid JSON Schema 2020-12 document with resolvable local references.", "/schema");
            }
        }

        public Result ValidatePayload(string schema, string payload)
        {
            var validation = ValidateSchema(schema);
            if (validation.IsFailure)
                return validation;

            try
            {
                var options = CreateOptions();
                var result = JsonSchema.FromText(schema).Evaluate(JsonNode.Parse(payload), options);
                return ToResult(result, "/payload");
            }
            catch (Exception ex) when (IsSchemaException(ex))
            {
                return Invalid("Payload could not be validated against the configured schema. Check its local references and payload JSON.", "/payload");
            }
        }

        private static EvaluationOptions CreateOptions()
        {
            var options = new EvaluationOptions
            {
                EvaluateAs = SpecVersion.Draft202012,
                OutputFormat = OutputFormat.List,
                ValidateAgainstMetaSchema = false,
                ProcessCustomKeywords = false
            };
            options.SchemaRegistry.Fetch = _ => null;
            return options;
        }

        private static Result ValidateSchemaPolicy(JsonNode? node, string path, bool root = false)
        {
            if (node is JsonValue boolean && boolean.TryGetValue<bool>(out _))
                return Result.Success();
            if (node is not JsonObject obj)
                return Invalid("A schema must be an object or boolean.", path);

            if (obj.TryGetPropertyValue("$schema", out var dialect)
                && (dialect is not JsonValue value || !value.TryGetValue<string>(out var uri) || uri != Dialect))
                return Invalid("Only JSON Schema 2020-12 is supported.", path + "/$schema");

            if (!root && obj.ContainsKey("$id"))
                return Invalid("Nested schema identifiers are not supported; use root-local references.", path + "/$id");

            foreach (var keyword in new[] { "$ref", "$dynamicRef" })
            {
                if (obj.TryGetPropertyValue(keyword, out var reference)
                    && (reference is not JsonValue referenceValue
                        || !referenceValue.TryGetValue<string>(out var target)
                        || !target.StartsWith('#')))
                    return Invalid("Only root-local schema references are allowed. Remote references are disabled.", path + "/" + keyword);
            }

            if (obj.TryGetPropertyValue("$vocabulary", out var vocabulary) && vocabulary is JsonObject vocabularies
                && vocabularies.Any(v => !v.Key.StartsWith("https://json-schema.org/draft/2020-12/vocab/", StringComparison.Ordinal)))
                return Invalid("Custom schema vocabularies are not supported.", path + "/$vocabulary");

            foreach (var property in obj)
            {
                var childPath = path + "/" + Escape(property.Key);
                if (SchemaMaps.Contains(property.Key) && property.Value is JsonObject map)
                {
                    foreach (var child in map)
                    {
                        var result = ValidateSchemaPolicy(child.Value, childPath + "/" + Escape(child.Key));
                        if (result.IsFailure) return result;
                    }
                }
                else if (SchemaArrays.Contains(property.Key) && property.Value is JsonArray array)
                {
                    for (var index = 0; index < array.Count; index++)
                    {
                        var result = ValidateSchemaPolicy(array[index], childPath + "/" + index);
                        if (result.IsFailure) return result;
                    }
                }
                else if (SchemaValues.Contains(property.Key))
                {
                    var result = ValidateSchemaPolicy(property.Value, childPath);
                    if (result.IsFailure) return result;
                }
            }

            return Result.Success();
        }

        private static Result ValidateLocalReferences(JsonNode? root)
        {
            var nodes = SchemaNodes(root).ToArray();
            var schemas = nodes.OfType<JsonObject>().ToArray();
            var nodeSet = nodes.ToHashSet();
            var graph = nodes.ToDictionary(node => node, _ => new List<(JsonNode Target, bool Progresses)>());

            foreach (var schema in schemas)
            {
                foreach (var edge in GetStructuralEdges(schema))
                    if (nodeSet.Contains(edge.Target))
                        graph[schema].Add(edge);
            }

            foreach (var schema in schemas)
            foreach (var keyword in new[] { "$ref", "$dynamicRef" })
            {
                if (schema[keyword] is not JsonValue reference)
                    continue;

                var value = reference.GetValue<string>();
                if (!value.StartsWith('#'))
                    return Invalid("Only root-local schema references are allowed. Remote references are disabled.", "/schema");

                var fragment = System.Uri.UnescapeDataString(value[1..]);
                JsonNode? target = root;
                if (fragment.Length == 0)
                    target = root;
                else if (!fragment.StartsWith('/'))
                {
                    target = schemas.SingleOrDefault(s =>
                        s["$anchor"]?.GetValue<string>() == fragment
                        || s["$dynamicAnchor"]?.GetValue<string>() == fragment);

                    if (target is null)
                        return Invalid("Schema reference does not resolve to a local anchor.", "/schema");
                }
                else
                {
                    foreach (var part in fragment[1..].Split('/'))
                    {
                        var token = part.Replace("~1", "/").Replace("~0", "~");
                        target = target switch
                        {
                            JsonObject obj when obj.TryGetPropertyValue(token, out var child) => child,
                            JsonArray array when int.TryParse(token, out var index) && index >= 0 && index < array.Count => array[index],
                            _ => null
                        };
                    }
                }

                if (target is null || !nodes.Any(s => ReferenceEquals(s, target)))
                    return Invalid("Schema reference does not resolve to a supported local subschema.", "/schema");

                graph[schema].Add((target, false));
            }

            if (HasNonProgressingCycle(graph))
                return Invalid("Schema references must not form a cycle without descending into a child instance.", "/schema");

            return Result.Success();
        }

        private static IEnumerable<(JsonNode Target, bool Progresses)> GetStructuralEdges(JsonObject schema)
        {
            if (schema["$defs"] is JsonObject defs)
                foreach (var child in defs)
                    if (child.Value is not null)
                        yield return (child.Value, false);

            if (schema["dependentSchemas"] is JsonObject dependent)
                foreach (var child in dependent)
                    if (child.Value is not null)
                        yield return (child.Value, false);

            foreach (var keyword in new[] { "allOf", "anyOf", "oneOf" })
                if (schema[keyword] is JsonArray array)
                    foreach (var child in array)
                        if (child is not null)
                            yield return (child, false);

            foreach (var keyword in new[] { "not", "if", "then", "else" })
                if (schema[keyword] is not null)
                    yield return (schema[keyword]!, false);

            foreach (var keyword in new[] { "properties", "patternProperties" })
                if (schema[keyword] is JsonObject map)
                    foreach (var child in map)
                        if (child.Value is not null)
                            yield return (child.Value, true);

            foreach (var keyword in new[] { "additionalProperties", "unevaluatedProperties", "propertyNames", "items", "contains", "unevaluatedItems", "contentSchema" })
                if (schema[keyword] is not null)
                    yield return (schema[keyword]!, true);

            if (schema["prefixItems"] is JsonArray prefixItems)
                foreach (var child in prefixItems)
                    if (child is not null)
                        yield return (child, true);
        }

        private static bool HasNonProgressingCycle(
            IReadOnlyDictionary<JsonNode, List<(JsonNode Target, bool Progresses)>> graph)
        {
            const int MaxReferenceTraversalDepth = 64;
            var visiting = new HashSet<JsonNode>();
            var visited = new HashSet<JsonNode>();

            bool Dfs(JsonNode node, int depth)
            {
                if (depth >= MaxReferenceTraversalDepth)
                    return true;
                if (visited.Contains(node))
                    return false;
                if (!visiting.Add(node))
                    return true;

                foreach (var edge in graph[node].Where(edge => !edge.Progresses))
                    if (Dfs(edge.Target, depth + 1))
                        return true;

                visiting.Remove(node);
                visited.Add(node);
                return false;
            }

            return graph.Keys.Any(node => Dfs(node, 0));
        }

        private static IEnumerable<JsonNode> SchemaNodes(JsonNode? node)
        {
            if (node is null) yield break;
            yield return node;
            if (node is not JsonObject obj) yield break;

            foreach (var property in obj)
            {
                IEnumerable<JsonNode?> children = property.Value switch
                {
                    JsonObject map when SchemaMaps.Contains(property.Key) => map.Select(p => p.Value),
                    JsonArray array when SchemaArrays.Contains(property.Key) => array,
                    _ when SchemaValues.Contains(property.Key) => [property.Value],
                    _ => []
                };

                foreach (var child in children)
                foreach (var descendant in SchemaNodes(child))
                    yield return descendant;
            }
        }

        private static Result ToResult(EvaluationResults evaluation, string prefix)
        {
            if (evaluation.IsValid)
                return Result.Success();

            var errors = Flatten(evaluation)
                .Where(result => !result.IsValid && result.HasErrors)
                .SelectMany(result => result.Errors!.Keys.Select(keyword =>
                    Error.Validation($"Value does not satisfy schema keyword '{keyword}'.") with
                    {
                        Path = prefix + result.InstanceLocation,
                        Keyword = keyword
                    }))
                .Distinct()
                .Take(MaxErrors)
                .ToArray();
            return errors.Length == 0
                ? Invalid("Value does not satisfy the configured schema.", prefix)
                : Result.Failure(errors);
        }

        private static IEnumerable<EvaluationResults> Flatten(EvaluationResults result)
        {
            yield return result;
            if (!result.HasDetails) yield break;
            foreach (var child in result.Details!)
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }

        private static bool IsSchemaException(Exception ex) =>
            ex is JsonException or JsonSchemaException or RefResolutionException or InvalidOperationException or ArgumentException;

        private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
        private static Result Invalid(string message, string path) =>
            Result.Failure(Error.Validation(message) with { Path = path });
    }
}

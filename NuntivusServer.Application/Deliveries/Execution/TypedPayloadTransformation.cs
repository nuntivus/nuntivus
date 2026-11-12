using System.Text.Json;
using System.Text.Json.Nodes;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Deliveries.Execution
{
    public static class TypedPayloadTransformation
    {
        public const int MaxNodes = 1000;
        public const int MaxDepth = 32;
        public const int MaxSteps = 10000;
        public const int MaxLength = 1_000_000;
        private static readonly HashSet<string> Operations = ["select", "constant", "default", "if", "map", "object", "array", "concat", "add", "equals", "and", "not"];

        public static Result Validate(JsonElement expression)
        {
            try
            {
                if (expression.GetRawText().Length > MaxLength) throw new TransformationException("Transformation configuration exceeds the size limit.");
                var nodes = 0;
                ValidateNode(expression, 0, ref nodes);
                return Result.Success();
            }
            catch (Exception ex) when (ex is TransformationException or InvalidOperationException)
            {
                return Result.Failure(Error.Validation(ex.Message));
            }
        }

        public static Result<string> Apply(string payload, JsonElement expression)
        {
            var validation = Validate(expression);
            if (validation.IsFailure) return Result<string>.Fail(validation.Errors);
            try
            {
                if (payload.Length > MaxLength) throw new TransformationException("Transformation input exceeds the size limit.");
                var root = JsonNode.Parse(payload);
                var budget = new Budget();
                var result = Evaluate(expression, new Value(true, root), new Value(true, root), budget, 0);
                if (!result.Exists) throw new TransformationException("Transformation result is missing; configure a default explicitly.");
                return Result.Success(Serialize(result.Node));
            }
            catch (Exception ex) when (ex is TransformationException or JsonException or OverflowException)
            {
                return Result.Failure<string>(Error.Validation(ex is OverflowException ? "Transformation numeric result is outside the decimal range." : ex.Message));
            }
        }

        private static void ValidateNode(JsonElement node, int depth, ref int count)
        {
            if (++count > MaxNodes || depth > MaxDepth) throw new TransformationException("Transformation exceeds the expression complexity limit.");
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("op", out var operation) || operation.ValueKind != JsonValueKind.String
                || !Operations.Contains(operation.GetString()!)) throw new TransformationException("Every expression must specify a supported string op.");
            var op = operation.GetString()!;
            string[] fields = op switch
            {
                "select" => ["path", "scope"], "constant" => ["value"], "default" => ["value", "fallback"],
                "if" => ["condition", "then", "else"], "map" => ["source", "value"], "object" => ["fields"],
                "not" => ["value"], _ => ["args"]
            };
            foreach (var property in node.EnumerateObject())
                if (property.Name != "op" && !fields.Contains(property.Name)) throw new TransformationException($"Unknown field '{property.Name}' for operation '{op}'.");
            foreach (var field in fields.Where(field => field != "scope"))
                if (!node.TryGetProperty(field, out _)) throw new TransformationException($"Operation '{op}' requires '{field}'.");
            if (op == "constant") return;
            if (op == "select")
            {
                var path = node.GetProperty("path");
                if (path.ValueKind != JsonValueKind.String || path.GetString()!.Length > 1000
                    || (path.GetString()!.Length > 0 && !path.GetString()!.StartsWith('/')))
                    throw new TransformationException("Selectors must be JSON pointers, or an empty string for the whole value.");
                if (node.TryGetProperty("scope", out var scope) && (scope.ValueKind != JsonValueKind.String || scope.GetString() is not ("root" or "item")))
                    throw new TransformationException("Selector scope must be root or item.");
                var pointer = path.GetString()!;
                for (var index = 0; index < pointer.Length; index++)
                    if (pointer[index] == '~' && (++index >= pointer.Length || pointer[index] is not ('0' or '1')))
                        throw new TransformationException("Selector contains an invalid JSON pointer escape.");
                return;
            }
            if (op == "object")
            {
                var members = node.GetProperty("fields");
                if (members.ValueKind != JsonValueKind.Object) throw new TransformationException("Object fields must be an object of expressions.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in members.EnumerateObject())
                {
                    if (!names.Add(member.Name)) throw new TransformationException("Object field names must be unique.");
                    ValidateNode(member.Value, depth + 1, ref count);
                }
                return;
            }
            if (fields[0] == "args")
            {
                var args = node.GetProperty("args");
                if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() > 100
                    || (op == "equals" ? args.GetArrayLength() != 2 : args.GetArrayLength() == 0))
                    throw new TransformationException("Operation arguments must be a non-empty array of at most 100 expressions (equals requires two).");
                foreach (var argument in args.EnumerateArray()) ValidateNode(argument, depth + 1, ref count);
                return;
            }
            foreach (var field in fields) ValidateNode(node.GetProperty(field), depth + 1, ref count);
        }

        private static Value Evaluate(JsonElement expression, Value root, Value item, Budget budget, int depth)
        {
            if (++budget.Steps > MaxSteps || depth > MaxDepth) throw new TransformationException("Transformation exceeds the execution limit.");
            Value Child(string name) => Evaluate(expression.GetProperty(name), root, item, budget, depth + 1);
            Value[] Arguments() => expression.GetProperty("args").EnumerateArray().Select(arg => Evaluate(arg, root, item, budget, depth + 1)).ToArray();
            var op = expression.GetProperty("op").GetString();
            Value result;
            switch (op)
            {
                case "select":
                    result = Select(expression.TryGetProperty("scope", out var scope) && scope.GetString() == "item" ? item : root,
                        expression.GetProperty("path").GetString()!);
                    break;
                case "constant": result = new(true, JsonNode.Parse(expression.GetProperty("value").GetRawText())); break;
                case "default":
                    result = Child("value");
                    if (!result.Exists) result = Child("fallback");
                    break;
                case "if": result = Boolean(Child("condition")) ? Child("then") : Child("else"); break;
                case "map":
                    var source = Required(Child("source"));
                    if (source is not JsonArray sourceArray) throw new TransformationException("Map source must be an array.");
                    var mapped = new JsonArray();
                    foreach (var element in sourceArray)
                    {
                        mapped.Add(Required(Evaluate(expression.GetProperty("value"), root, new Value(true, element), budget, depth + 1))?.DeepClone());
                        CheckSize(mapped);
                    }
                    result = new(true, mapped);
                    break;
                case "object":
                    var obj = new JsonObject();
                    foreach (var field in expression.GetProperty("fields").EnumerateObject())
                    {
                        obj[field.Name] = Required(Evaluate(field.Value, root, item, budget, depth + 1))?.DeepClone();
                        CheckSize(obj);
                    }
                    result = new(true, obj);
                    break;
                case "array": result = new(true, new JsonArray(Arguments().Select(value => Required(value)?.DeepClone()).ToArray())); break;
                case "concat":
                    var text = new System.Text.StringBuilder();
                    foreach (var argument in Arguments())
                    {
                        if (Required(argument) is not JsonValue scalar || !scalar.TryGetValue<string>(out var value))
                            throw new TransformationException("Concat arguments must be strings.");
                        text.Append(value);
                        if (text.Length > MaxLength) throw new TransformationException("Transformation output exceeds the size limit.");
                    }
                    result = new(true, JsonValue.Create(text.ToString()));
                    break;
                case "add":
                    decimal sum = 0;
                    foreach (var argument in Arguments())
                    {
                        if (Required(argument) is not JsonValue scalar || !scalar.TryGetValue<decimal>(out var value))
                            throw new TransformationException("Add arguments must be decimal-range numbers.");
                        sum = checked(sum + value);
                    }
                    result = new(true, JsonValue.Create(sum));
                    break;
                case "equals":
                    var values = Arguments();
                    result = new(true, JsonValue.Create(JsonNode.DeepEquals(Required(values[0]), Required(values[1]))));
                    break;
                case "and": result = new(true, JsonValue.Create(Arguments().All(Boolean))); break;
                case "not": result = new(true, JsonValue.Create(!Boolean(Child("value")))); break;
                default: throw new TransformationException("Unsupported transformation operation.");
            }
            if (result.Exists) CheckSize(result.Node);
            return result;
        }

        private static Value Select(Value value, string pointer)
        {
            if (pointer.Length == 0) return value;
            foreach (var token in pointer[1..].Split('/'))
            {
                var segment = token.Replace("~1", "/").Replace("~0", "~");
                if (!value.Exists) return value;
                if (value.Node is JsonObject obj)
                    value = obj.TryGetPropertyValue(segment, out var member) ? new(true, member) : new(false, null);
                else if (value.Node is JsonArray array && segment.Length > 0 && (segment == "0" || segment[0] is >= '1' and <= '9')
                    && segment.All(char.IsAsciiDigit) && int.TryParse(segment, out var index) && index < array.Count)
                    value = new(true, array[index]);
                else return new(false, null);
            }
            return value;
        }

        private static JsonNode? Required(Value value) => value.Exists ? value.Node : throw new TransformationException("A selected field is missing; configure a default explicitly.");
        private static bool Boolean(Value value) => Required(value) is JsonValue scalar && scalar.TryGetValue<bool>(out var boolean)
            ? boolean : throw new TransformationException("Conditions and boolean operations require boolean values.");
        private static string Serialize(JsonNode? node)
        {
            var json = node?.ToJsonString() ?? "null";
            if (json.Length > MaxLength) throw new TransformationException("Transformation output exceeds the size limit.");
            return json;
        }
        private static void CheckSize(JsonNode? node) => _ = Serialize(node);
        private readonly record struct Value(bool Exists, JsonNode? Node);
        private sealed class Budget { public int Steps; }
        private sealed class TransformationException(string message) : Exception(message);
    }
}

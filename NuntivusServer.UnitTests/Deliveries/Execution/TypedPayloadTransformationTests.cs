using System.Text.Json;
using System.Text.Json.Nodes;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.UnitTests.Deliveries.Execution
{
    public sealed class TypedPayloadTransformationTests
    {
        [Theory]
        [InlineData("{}", "{\"op\":\"default\",\"value\":{\"op\":\"select\",\"path\":\"/name\"},\"fallback\":{\"op\":\"constant\",\"value\":\"unknown\"}}", "\"unknown\"")]
        [InlineData("{\"name\":null}", "{\"op\":\"default\",\"value\":{\"op\":\"select\",\"path\":\"/name\"},\"fallback\":{\"op\":\"constant\",\"value\":\"unknown\"}}", "null")]
        [InlineData("{\"a/b\":3}", "{\"op\":\"select\",\"path\":\"/a~1b\"}", "3")]
        [InlineData("{}", "{\"op\":\"add\",\"args\":[{\"op\":\"constant\",\"value\":1.25},{\"op\":\"constant\",\"value\":2}]}", "3.25")]
        [InlineData("{}", "{\"op\":\"if\",\"condition\":{\"op\":\"constant\",\"value\":true},\"then\":{\"op\":\"constant\",\"value\":42},\"else\":{\"op\":\"select\",\"path\":\"/missing\"}}", "42")]
        [InlineData("{\"values\":[1,null,3]}", "{\"op\":\"map\",\"source\":{\"op\":\"select\",\"path\":\"/values\"},\"value\":{\"op\":\"select\",\"scope\":\"item\",\"path\":\"\"}}", "[1,null,3]")]
        public void Evaluates_typed_operations(string payload, string expression, string expected)
        {
            var result = Apply(payload, expression);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(result.Value)));
        }

        [Fact]
        public void Maps_objects_using_root_and_item_scope()
        {
            const string expression = """
                {"op":"object","fields":{"items":{"op":"map","source":{"op":"select","path":"/items"},"value":
                    {"op":"object","fields":{"label":{"op":"concat","args":[{"op":"select","path":"/prefix"},{"op":"select","scope":"item","path":"/name"}]}}}}}}
                """;
            var result = Apply("""{"prefix":"order-","items":[{"name":"a"},{"name":"b"}]}""", expression);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"items":[{"label":"order-a"},{"label":"order-b"}]}"""), JsonNode.Parse(result.Value)));
        }

        [Theory]
        [InlineData("{\"op\":\"execute\",\"code\":\"anything\"}")]
        [InlineData("{\"op\":\"select\",\"path\":\"name\"}")]
        [InlineData("{\"op\":\"select\",\"path\":\"/~2\"}")]
        [InlineData("{\"op\":\"constant\"}")]
        [InlineData("{\"op\":\"constant\",\"value\":1,\"extra\":true}")]
        [InlineData("{\"op\":\"equals\",\"args\":[]}")]
        public void Rejects_invalid_configurations(string expression) =>
            Assert.True(TypedPayloadTransformation.Validate(JsonDocument.Parse(expression).RootElement).IsFailure);

        [Theory]
        [InlineData("{}", "{\"op\":\"select\",\"path\":\"/missing\"}")]
        [InlineData("{}", "{\"op\":\"not\",\"value\":{\"op\":\"constant\",\"value\":null}}")]
        [InlineData("{}", "{\"op\":\"concat\",\"args\":[{\"op\":\"constant\",\"value\":42}]}")]
        [InlineData("{}", "{\"op\":\"map\",\"source\":{\"op\":\"constant\",\"value\":null},\"value\":{\"op\":\"constant\",\"value\":1}}")]
        public void Reports_runtime_errors_without_implicit_type_coercion(string payload, string expression) => Assert.True(Apply(payload, expression).IsFailure);

        [Fact]
        public void Runtime_budget_stops_large_array_maps()
        {
            var payload = JsonSerializer.Serialize(Enumerable.Range(0, 10001));
            var result = Apply(payload, """{"op":"map","source":{"op":"select","path":""},"value":{"op":"select","scope":"item","path":""}}""");
            Assert.True(result.IsFailure);
            Assert.Contains("execution limit", result.Error!.Message);
        }

        [Fact]
        public void Content_identity_changes_on_edit_and_survives_serialization()
        {
            var first = new ContractPayloadTransformation(2, [], JsonDocument.Parse("""{"op":"constant","value":1}""").RootElement);
            var second = new ContractPayloadTransformation(2, [], JsonDocument.Parse("""{"op":"constant","value":2}""").RootElement);
            Assert.NotEqual(first.Identity, second.Identity);
            Assert.Equal(first.Identity, JsonSerializer.Deserialize<ContractPayloadTransformation>(JsonSerializer.Serialize(first))!.Identity);
            var result = new DeliveryPayloadTransformer().Apply("{}", JsonSerializer.Serialize(first));
            Assert.True(result.IsSuccess);
            Assert.Equal("1", result.Value);
        }

        private static NuntivusServer.Common.Result<string> Apply(string payload, string expression)
        {
            using var document = JsonDocument.Parse(expression);
            return TypedPayloadTransformation.Apply(payload, document.RootElement);
        }
    }
}

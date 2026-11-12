using NuntivusServer.Application.Events;
using NuntivusServer.Contracts.Events;
using NuntivusServer.Infastructure.Events;

namespace NuntivusServer.UnitTests.Events
{
    public sealed class SchemaCompatibilityAnalyzerTests
    {
        private readonly SchemaCompatibilityAnalyzer _analyzer = new();

        [Theory]
        [InlineData("{}", "{}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("false", "{}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{}", "true", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{}", "false", SchemaCompatibilityStatus.Breaking)]
        [InlineData("{\"type\":\"integer\"}", "{\"type\":\"number\"}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{\"type\":\"number\"}", "{\"type\":\"integer\"}", SchemaCompatibilityStatus.Breaking)]
        [InlineData("{\"type\":[\"string\",\"null\"]}", "{\"type\":\"string\"}", SchemaCompatibilityStatus.Breaking)]
        [InlineData("{\"enum\":[1,2]}", "{\"enum\":[1,2,3]}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{\"enum\":[1,2]}", "{\"enum\":[1]}", SchemaCompatibilityStatus.Breaking)]
        [InlineData("{\"const\":null}", "{\"enum\":[null,1]}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{\"type\":\"number\",\"minimum\":0}", "{\"type\":\"number\",\"minimum\":-1}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{\"type\":\"number\",\"minimum\":0}", "{\"type\":\"number\",\"exclusiveMinimum\":0}", SchemaCompatibilityStatus.Breaking)]
        [InlineData("{\"type\":\"number\",\"multipleOf\":4}", "{\"type\":\"number\",\"multipleOf\":2}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{\"type\":\"number\",\"minimum\":0}", "{\"type\":\"number\",\"minimum\":1e-50}", SchemaCompatibilityStatus.Indeterminate)]
        [InlineData("{\"type\":\"string\",\"maxLength\":10}", "{\"type\":\"string\",\"maxLength\":20}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{\"type\":\"string\"}", "{\"type\":\"string\",\"pattern\":\"abc\"}", SchemaCompatibilityStatus.Indeterminate)]
        [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}", "{\"type\":\"array\",\"items\":{\"type\":\"number\"}}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{\"type\":\"array\"}", "{\"type\":\"array\",\"uniqueItems\":true}", SchemaCompatibilityStatus.Breaking)]
        [InlineData("{}", "{\"oneOf\":[{\"type\":\"integer\"},{\"type\":\"string\"}]}", SchemaCompatibilityStatus.Indeterminate)]
        [InlineData("{\"type\":\"object\"}", "{\"type\":\"object\",\"required\":[\"id\"]}", SchemaCompatibilityStatus.Breaking)]
        [InlineData("{\"type\":\"object\",\"additionalProperties\":false}", "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"id\":{\"type\":\"string\"}}}", SchemaCompatibilityStatus.Compatible)]
        [InlineData("{\"type\":\"object\"}", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}}", SchemaCompatibilityStatus.Breaking)]
        [InlineData("{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}},\"additionalProperties\":false}", "{\"type\":\"object\",\"additionalProperties\":false}", SchemaCompatibilityStatus.Breaking)]
        public void Compares_backward_acceptance(string previous, string proposed, SchemaCompatibilityStatus expected)
        {
            var report = _analyzer.Compare("orders.created", 2, 1, previous, proposed, SchemaCompatibilityMode.Backward);
            Assert.Equal(expected, report.Status);
            Assert.Equal(1, report.AgainstVersion);
            Assert.Equal(2, report.Version);
            Assert.All(report.Findings, finding => Assert.StartsWith("/", finding.Path));
        }

        [Fact]
        public void Forward_mode_reverses_the_acceptance_direction()
        {
            const string previous = """{"type":"number"}""";
            const string proposed = """{"type":"integer"}""";
            Assert.Equal(SchemaCompatibilityStatus.Breaking, _analyzer.Compare("orders.created", 2, 1, previous, proposed, SchemaCompatibilityMode.Backward).Status);
            Assert.Equal(SchemaCompatibilityStatus.Compatible, _analyzer.Compare("orders.created", 2, 1, previous, proposed, SchemaCompatibilityMode.Forward).Status);
        }

        [Fact]
        public void Nested_changes_have_escaped_field_paths()
        {
            var report = _analyzer.Compare("orders.created", 2, 1,
                """{"type":"object","properties":{"a/b":{"type":"number"}}}""",
                """{"type":"object","properties":{"a/b":{"type":"string"}}}""", SchemaCompatibilityMode.Backward);
            Assert.Contains(report.Findings, finding => finding.Path == "/properties/a~1b/type");
        }

        [Fact]
        public void Unchanged_complex_schemas_are_compatible()
        {
            const string schema = """{"oneOf":[{"type":"integer"},{"type":"string"}]}""";
            Assert.Equal(SchemaCompatibilityStatus.Compatible, _analyzer.Compare("orders.created", 2, 1, schema, schema, SchemaCompatibilityMode.Backward).Status);
        }

        [Fact]
        public void Compatible_reports_preserve_acceptance_for_representative_payloads()
        {
            string[] schemas = ["true", "false", "{}", """{"type":"number"}""", """{"type":"integer"}""",
                """{"type":"number","minimum":0}""", """{"type":"number","maximum":10}""",
                """{"type":"string","minLength":1}""", """{"enum":[1,2,null]}""",
                """{"type":"object","properties":{"id":{"type":"integer"}},"additionalProperties":false}""",
                """{"type":"object","required":["id"],"properties":{"id":{"type":"number"}}}""",
                """{"type":"array","items":{"type":"number"},"maxItems":2}""",
                """{"type":"array","items":{"type":"integer"},"uniqueItems":true}"""];
            string[] payloads = ["null", "true", "0", "-1", "1", "1.5", "11", "\"\"", "\"value\"", "{}",
                """{"id":1}""", """{"id":"value"}""", """{"other":1}""", "[]", "[1,2]", "[1,1]", "[1.5]", "[1,2,3]"];
            var validator = new JsonEventSchemaValidator();
            foreach (var source in schemas)
            foreach (var target in schemas)
            {
                if (_analyzer.Compare("orders.created", 2, 1, source, target, SchemaCompatibilityMode.Backward).Status != SchemaCompatibilityStatus.Compatible) continue;
                foreach (var payload in payloads.Where(payload => validator.ValidatePayload(source, payload).IsSuccess))
                    Assert.True(validator.ValidatePayload(target, payload).IsSuccess,
                        $"A compatible report must preserve acceptance. Source: {source}; target: {target}; payload: {payload}");
            }
        }
    }
}

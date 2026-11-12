using NuntivusServer.Domain.Events;
using NuntivusServer.Infastructure.Events;

namespace NuntivusServer.UnitTests.Events
{
    public sealed class JsonEventSchemaValidatorTests
    {
        private readonly JsonEventSchemaValidator _validator = new();

        [Theory]
        [InlineData("{}")]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\"}")]
        [InlineData("{\"const\":{\"$ref\":\"https://example.com/data-not-schema\"}}")]
        public void Accepts_supported_schema_documents(string schema) =>
            Assert.True(_validator.ValidateSchema(schema).IsSuccess);

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("42")]
        [InlineData("not-json")]
        [InlineData("{\"type\":\"not-a-type\"}")]
        [InlineData("{\"required\":\"invalid\"}")]
        [InlineData("{\"minimum\":\"invalid\"}")]
        [InlineData("{\"$schema\":\"http://json-schema.org/draft-07/schema#\"}")]
        [InlineData("{\"$ref\":\"https://example.com/schema\"}")]
        [InlineData("{\"properties\":{\"child\":{\"$ref\":\"file:///etc/passwd\"}}}")]
        [InlineData("{\"$defs\":{\"child\":{\"$id\":\"https://example.com/nested\"}}}")]
        [InlineData("{\"$ref\":\"#\"}")]
        [InlineData("{\"allOf\":[{\"$ref\":\"#\"}]}")]
        [InlineData("{\"$defs\":{\"a\":{\"$ref\":\"#/$defs/b\"},\"b\":{\"$ref\":\"#/$defs/a\"}}}")]
        public void Rejects_invalid_unsupported_or_external_schema_documents(string schema)
        {
            var result = _validator.ValidateSchema(schema);
            Assert.True(result.IsFailure);
            Assert.All(result.Errors, error => Assert.StartsWith("/schema", error.Path));
        }

        [Fact]
        public void Validates_nested_payloads_and_returns_structured_paths_without_values()
        {
            const string schema = """
                {"type":"object","properties":{"customer":{"type":"object","required":["id"],
                "properties":{"id":{"type":"integer"}}}},"required":["customer"]}
                """;
            var valid = _validator.ValidatePayload(schema, """{"customer":{"id":12}}""");
            var invalid = _validator.ValidatePayload(schema, """{"customer":{"id":"sensitive"}}""");
            Assert.True(valid.IsSuccess);
            Assert.True(invalid.IsFailure);
            Assert.Contains(invalid.Errors, error => error.Path == "/payload/customer/id" && error.Keyword == "type");
            Assert.All(invalid.Errors, error => Assert.DoesNotContain("sensitive", error.Message));
        }

        [Fact]
        public void Supports_2020_12_prefix_items_and_unevaluated_properties()
        {
            const string schema = """
                {"type":"object","properties":{"items":{"type":"array","prefixItems":[{"type":"integer"}],"items":false}},
                "unevaluatedProperties":false}
                """;
            Assert.True(_validator.ValidatePayload(schema, """{"items":[1]}""").IsSuccess);
            Assert.True(_validator.ValidatePayload(schema, """{"items":["wrong"]}""").IsFailure);
            Assert.True(_validator.ValidatePayload(schema, """{"items":[1,2]}""").IsFailure);
            Assert.True(_validator.ValidatePayload(schema, """{"extra":true}""").IsFailure);
        }

        [Fact]
        public void Resolves_local_references_without_network_access()
        {
            const string schema = """
                {"$defs":{"identifier":{"type":"string"}},"type":"object",
                "properties":{"id":{"$ref":"#/$defs/identifier"}}}
                """;
            Assert.True(_validator.ValidatePayload(schema, """{"id":"42"}""").IsSuccess);
            Assert.True(_validator.ValidatePayload(schema, """{"id":42}""").IsFailure);
        }

        [Fact]
        public void False_schema_rejects_every_payload() =>
            Assert.True(_validator.ValidatePayload("false", "{}").IsFailure);

        [Fact]
        public void Supports_recursive_schemas_that_descend_into_child_instances()
        {
            const string schema = """{"type":"object","properties":{"child":{"$ref":"#"}}}""";
            Assert.True(_validator.ValidatePayload(schema, """{"child":{"child":{}}}""").IsSuccess);
        }

        [Fact]
        public void Missing_reference_fails_closed() =>
            Assert.True(_validator.ValidatePayload("{\"$ref\":\"#/$defs/missing\"}", "{}").IsFailure);

        [Fact]
        public void Historical_events_do_not_claim_successful_validation()
        {
            var historical = Event.Rehydrate(Guid.NewGuid(), "legacy", "orders.created", "source",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "{}", null);
            Assert.Equal(EventSchemaValidationStatus.Unknown, historical.SchemaValidationStatus);
            Assert.Null(historical.InputSchemaVersion);
        }

        [Fact]
        public void Schema_metadata_must_be_consistent()
        {
            Assert.Throws<ArgumentException>(() => Event.Create("key", "orders.created", "source",
                DateTimeOffset.UtcNow, "{}", (EventMetadata?)null, null, EventSchemaValidationStatus.Valid));
            Assert.Throws<ArgumentException>(() => Event.Create("key", "orders.created", "source",
                DateTimeOffset.UtcNow, "{}", (EventMetadata?)null, 1, EventSchemaValidationStatus.NotConfigured));
        }
    }
}

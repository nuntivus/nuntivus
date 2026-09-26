using System.Text.Json;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.UnitTests.Deliveries.Execution
{
    public sealed class DeliveryPayloadTransformerTests
    {
        private static readonly IDeliveryPayloadTransformer Transformer = new DeliveryPayloadTransformer();

        [Fact]
        public void Apply_without_transformation_returns_original_payload()
        {
            var payload = "{\"order\":{\"id\":\"o-1\"}}";

            var result = Transformer.Apply(payload, null);

            Assert.True(result.IsSuccess);
            Assert.Equal(payload, result.Value);
        }

        [Fact]
        public void Apply_with_valid_mappings_projects_payload()
        {
            var payload = "{\"order\":{\"id\":\"o-1\",\"amount\":120}}";
            var transformation = JsonSerializer.Serialize(new ContractPayloadTransformation(
                1,
                new[]
                {
                    new ContractPayloadFieldMapping("order.id", "payload.orderId"),
                    new ContractPayloadFieldMapping("order.amount", "payload.total")
                }));

            var result = Transformer.Apply(payload, transformation);

            Assert.True(result.IsSuccess);
            using var doc = JsonDocument.Parse(result.Value);
            var root = doc.RootElement;
            Assert.Equal("o-1", root.GetProperty("payload").GetProperty("orderId").GetString());
            Assert.Equal(120, root.GetProperty("payload").GetProperty("total").GetInt32());
        }

        [Fact]
        public void Apply_with_missing_source_selector_returns_validation_failure()
        {
            var payload = "{\"order\":{\"id\":\"o-1\"}}";
            var transformation = JsonSerializer.Serialize(new ContractPayloadTransformation(
                1,
                new[]
                {
                    new ContractPayloadFieldMapping("order.missing", "payload.orderId")
                }));

            var result = Transformer.Apply(payload, transformation);

            Assert.True(result.IsFailure);
            Assert.Contains("source selector", result.Error!.Message);
        }
    }
}

using NuntivusServer.Application.Sources;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.UnitTests.Sources
{
    public sealed class InboundSourceEnvelopeTests
    {
        private static readonly string SourceId = Guid.NewGuid().ToString("D");

        [Fact]
        public void ValidEnvelopeUsesStableFallbackKey()
        {
            var result = InboundSourceEnvelope.Parse(new InboundSourceEventContext(SourceId,
                "{\"type\":\"orders.created\",\"payload\":{\"id\":1}}", "broker-123"));
            Assert.True(result.IsSuccess);
            Assert.Equal("broker-123", result.Value.IdempotencyKey);
            Assert.Equal(SourceId, result.Value.SourceId.Value);
        }

        [Theory]
        [InlineData("{\"type\":\"orders.created\",\"payload\":[]}")]
        [InlineData("{\"type\":\"orders.created\",\"payload\":{},\"metadata\":{\"password\":\"x\"}}")]
        [InlineData("{\"type\":\"orders.created\",\"payload\":{},\"timestamp\":\"invalid\"}")]
        public void InvalidEnvelopeIsRejected(string body)
        {
            var result = InboundSourceEnvelope.Parse(new InboundSourceEventContext(SourceId, body, "broker-123"));
            Assert.True(result.IsFailure);
        }
    }
}

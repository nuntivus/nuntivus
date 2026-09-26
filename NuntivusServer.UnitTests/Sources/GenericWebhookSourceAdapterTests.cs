using System;
using NuntivusServer.Application.Sources;

namespace NuntivusServer.UnitTests.Sources
{
    public sealed class GenericWebhookSourceAdapterTests
    {
        private readonly GenericWebhookSourceAdapter _sut = new();

        [Fact]
        public async Task NormalizeAsync_WithValidContext_NormalizesCanonicalFields()
        {
            var source = Guid.NewGuid().ToString("D").ToUpperInvariant();

            var result = await _sut.NormalizeAsync(
                new GenericWebhookSourceEventContext(
                    Source: $"  {source}  ",
                    IdempotencyKey: "  webhook-idem-1  ",
                    Type: "  orders.updated  ",
                    Timestamp: DateTimeOffset.Parse("2026-01-01T10:00:00Z"),
                    Payload: "{\"orderId\":\"o-1\"}",
                    Metadata: "{\"attempt\":2,\"traceId\":\"t-1\"}"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            var canonical = result.EnsureValue();
            Assert.Equal(source.ToLowerInvariant(), canonical.SourceId.Value);
            Assert.Equal("webhook-idem-1", canonical.IdempotencyKey);
            Assert.Equal("orders.updated", canonical.Type);
            Assert.Equal("{\"orderId\":\"o-1\"}", canonical.Payload);
            Assert.Equal("{\"attempt\":\"2\",\"traceId\":\"t-1\"}", canonical.Metadata);
        }

        [Fact]
        public async Task NormalizeAsync_WithInvalidContext_ReturnsValidationError()
        {
            var result = await _sut.NormalizeAsync(new object(), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal("validation", result.Error?.Code);
        }

        [Fact]
        public async Task NormalizeAsync_WithNonGuidSource_ReturnsValidationError()
        {
            var result = await _sut.NormalizeAsync(
                new GenericWebhookSourceEventContext(
                    Source: "orders-webhook",
                    IdempotencyKey: "webhook-idem-1",
                    Type: "orders.updated",
                    Timestamp: DateTimeOffset.UtcNow,
                    Payload: "{}",
                    Metadata: null),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("validation", result.Error?.Code);
        }
    }
}

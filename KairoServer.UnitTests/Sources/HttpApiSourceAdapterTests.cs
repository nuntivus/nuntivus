using System;
using KairoServer.Application.Sources;

namespace KairoServer.UnitTests.Sources
{
    public sealed class HttpApiSourceAdapterTests
    {
        private readonly HttpApiSourceAdapter _sut = new();

        [Fact]
        public async Task NormalizeAsync_WithValidContext_NormalizesCanonicalFields()
        {
            var source = Guid.NewGuid().ToString("D").ToUpperInvariant();

            var result = await _sut.NormalizeAsync(
                new HttpApiSourceEventContext(
                    IdempotencyKey: "  idem-1  ",
                    Type: "  orders.created  ",
                    Source: $"  {source}  ",
                    Timestamp: DateTimeOffset.Parse("2026-01-01T10:00:00Z"),
                    Payload: "{\"orderId\":\"o-1\"}",
                    Metadata: "[1,2,3]"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            var canonical = result.EnsureValue();
            Assert.Equal("idem-1", canonical.IdempotencyKey);
            Assert.Equal("orders.created", canonical.Type);
            Assert.Equal(source.ToLowerInvariant(), canonical.SourceId.Value);
            Assert.Equal("{\"orderId\":\"o-1\"}", canonical.Payload);
            Assert.Equal("{}", canonical.Metadata);
        }

        [Fact]
        public async Task NormalizeAsync_WithInvalidSource_ReturnsValidationFailure()
        {
            var result = await _sut.NormalizeAsync(
                new HttpApiSourceEventContext(
                    IdempotencyKey: "idem-1",
                    Type: "orders.created",
                    Source: "orders-api",
                    Timestamp: DateTimeOffset.UtcNow,
                    Payload: "{}",
                    Metadata: null),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("validation", result.Error?.Code);
        }

        [Fact]
        public async Task NormalizeAsync_WithWrongContext_ReturnsValidationFailure()
        {
            var result = await _sut.NormalizeAsync(new object(), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("validation", result.Error?.Code);
        }
    }
}

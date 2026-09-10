using System;
using KairoServer.Domain.Events;

namespace KairoServer.UnitTests.Events
{
    public class EventSourcePreservationTests
    {
        [Fact]
        public void Create_Preserves_SourceId_Verbatim()
        {
            const string sourceId = "orders.v1";

            var evt = Event.Create(
                idempotencyKey: "idem-1",
                type: "order.created",
                sourceId: sourceId,
                occurredAt: DateTimeOffset.UtcNow,
                payload: "{}",
                metadata: (string?)null);

            Assert.Equal(sourceId, evt.SourceId);
        }

        [Fact]
        public void Create_Does_Not_Normalize_Or_Alter_SourceId()
        {
            // The canonical Event preserves whatever the ingestion layer chose as the
            // source identifier. Normalization is the caller/adapter's responsibility.
            const string sourceId = "Orders.V1";

            var evt = Event.Create(
                idempotencyKey: "idem-1",
                type: "order.created",
                sourceId: sourceId,
                occurredAt: DateTimeOffset.UtcNow,
                payload: "{}",
                metadata: (string?)null);

            Assert.Equal(sourceId, evt.SourceId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Create_Throws_When_SourceId_Missing(string? sourceId)
        {
            Assert.Throws<ArgumentException>(() => Event.Create(
                idempotencyKey: "idem-1",
                type: "order.created",
                sourceId: sourceId!,
                occurredAt: DateTimeOffset.UtcNow,
                payload: "{}",
                metadata: (string?)null));
        }
    }
}

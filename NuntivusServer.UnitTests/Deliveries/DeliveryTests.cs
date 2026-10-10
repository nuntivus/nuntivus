using System;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Domain.Deliveries;

namespace NuntivusServer.UnitTests.Deliveries
{
    public class DeliveryTests
    {
        private static readonly DeliveryFactory DeliveryFactory = new();

        private static Destination NewDestination() =>
            new(DestinationType.Http, "{\"url\":\"https://example.test\"}");

        [Fact]
        public void CreatePending_Populates_Fields_And_Denormalizes_Destination()
        {
            var now = DateTimeOffset.UtcNow;
            var destination = NewDestination();
            const string authRef = "secret.acme.delivery";
            const int authVersion = 3;
            const int transformationVersion = 2;
            const int schemaVersion = 5;

            var delivery = DeliveryFactory.CreatePending(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "cust-1",
                "orders.created",
                "{}",
                destination,
                now,
                transformationConfiguration: "{\"Version\":2,\"FieldMappings\":[]}",
                rateLimit: null,
                ordering: null,
                authenticationReference: authRef,
                authenticationVersion: authVersion,
                transformationVersion: transformationVersion,
                schemaVersion: schemaVersion);

            Assert.Equal(DeliveryStatus.Pending, delivery.Status);
            Assert.Equal(0, delivery.AttemptCount);
            Assert.Null(delivery.LastError);
            Assert.Null(delivery.NextAttemptAt);
            Assert.Equal(destination.Type, delivery.DestinationType);
            Assert.Equal(destination.Configuration, delivery.DestinationConfiguration);
            Assert.Equal(authRef, delivery.AuthenticationReference);
            Assert.Equal(authVersion, delivery.AuthenticationVersion);
            Assert.Equal(transformationVersion, delivery.TransformationVersion);
            Assert.Equal(schemaVersion, delivery.SchemaVersion);
            Assert.Equal(now, delivery.CreatedAt);
            Assert.Equal(now, delivery.UpdatedAt);
        }

        [Fact]
        public void CreatePending_Generates_UuidV7_Ids()
        {
            var now = DateTimeOffset.UtcNow;
            var delivery = DeliveryFactory.CreatePending(
                Guid.NewGuid(), Guid.NewGuid(), "c", "t", "{}", NewDestination(), now);

            var bytes = delivery.Id.ToByteArray(bigEndian: true);
            // Version nibble sits in the high nibble of byte 6.
            Assert.Equal(0x70, bytes[6] & 0xF0);
            // RFC 4122 variant '10xx' in byte 8.
            Assert.Equal(0x80, bytes[8] & 0xC0);
        }

        [Fact]
        public void UuidV7_Ids_Are_Time_Ordered()
        {
            var t0 = DateTimeOffset.UtcNow;
            var a = DeliveryFactory.CreatePending(Guid.NewGuid(), Guid.NewGuid(), "c", "t", "{}", NewDestination(), t0);
            var b = DeliveryFactory.CreatePending(Guid.NewGuid(), Guid.NewGuid(), "c", "t", "{}", NewDestination(), t0.AddSeconds(1));

            // First 6 timestamp bytes (big-endian) must be non-decreasing.
            var ab = a.Id.ToByteArray(bigEndian: true);
            var bb = b.Id.ToByteArray(bigEndian: true);
            for (var i = 0; i < 6; i++)
            {
                if (ab[i] != bb[i])
                {
                    Assert.True(ab[i] < bb[i]);
                    return;
                }
            }
        }

        [Fact]
        public void CreatePending_Throws_On_Empty_EventId()
        {
            Assert.Throws<ArgumentException>(() => DeliveryFactory.CreatePending(
                Guid.Empty, Guid.NewGuid(), "c", "t", "{}", NewDestination(), DateTimeOffset.UtcNow));
        }

        [Fact]
        public void CreatePending_Throws_On_Null_Destination()
        {
            Assert.Throws<ArgumentNullException>(() => DeliveryFactory.CreatePending(
                Guid.NewGuid(), Guid.NewGuid(), "c", "t", "{}", null!, DateTimeOffset.UtcNow));
        }

        [Fact]
        public void MarkInFlight_From_Pending_Increments_Attempt()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now.AddSeconds(1));

            Assert.Equal(DeliveryStatus.InFlight, delivery.Status);
            Assert.Equal(1, delivery.AttemptCount);
            Assert.Null(delivery.NextAttemptAt);
        }

        [Fact]
        public void MarkDelivered_Terminal_From_InFlight()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now);
            delivery.MarkDelivered(now.AddSeconds(1));

            Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
            Assert.Null(delivery.LastError);
        }

        [Fact]
        public void MarkFailed_Records_Error_And_NextAttempt()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now);
            var next = now.AddMinutes(1);
            delivery.MarkFailed("boom", next, now.AddSeconds(1));

            Assert.Equal(DeliveryStatus.Failed, delivery.Status);
            Assert.Equal("boom", delivery.LastError);
            Assert.Equal(next, delivery.NextAttemptAt);
        }

        [Fact]
        public void Failed_Delivery_Can_Retry_Via_MarkInFlight()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now);
            delivery.MarkFailed("boom", now.AddMinutes(1), now);
            delivery.MarkInFlight(now.AddMinutes(1));

            Assert.Equal(DeliveryStatus.InFlight, delivery.Status);
            Assert.Equal(2, delivery.AttemptCount);
        }

        [Fact]
        public void MarkDelivered_From_Pending_Throws()
        {
            var delivery = NewPending(out var now);
            Assert.Throws<InvalidOperationException>(() => delivery.MarkDelivered(now));
        }

        [Fact]
        public void MarkInFlight_From_Delivered_Throws()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now);
            delivery.MarkDelivered(now);
            Assert.Throws<InvalidOperationException>(() => delivery.MarkInFlight(now));
        }

        [Fact]
        public void MarkDead_From_InFlight_Sets_Terminal_State()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now);
            delivery.MarkDead("fatal", now.AddSeconds(1));

            Assert.Equal(DeliveryStatus.Dead, delivery.Status);
            Assert.Equal("fatal", delivery.LastError);
            Assert.Null(delivery.NextAttemptAt);
        }

        [Fact]
        public void ManualRetryReset_From_Dead_Reactivates_And_Resets_AttemptCount()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now);
            delivery.MarkDead("fatal", now.AddSeconds(1));

            var retryAt = now.AddSeconds(2);
            var changed = delivery.ManualRetryReset(retryAt);

            Assert.True(changed);
            Assert.Equal(DeliveryStatus.Pending, delivery.Status);
            Assert.Equal(0, delivery.AttemptCount);
            Assert.Null(delivery.LastError);
            Assert.Equal(retryAt, delivery.NextAttemptAt);
        }

        [Fact]
        public void ManualRetryReset_From_Attempted_Unscheduled_Pending_Reschedules()
        {
            var original = NewPending(out var now);
            var delivery = Delivery.Rehydrate(
                original.Id, original.EventId, original.ContractId, original.CustomerId,
                original.EventType, original.Payload, original.DestinationType,
                original.DestinationConfiguration, DeliveryStatus.Pending, 1, null, null,
                original.CreatedAt, now);

            var retryAt = now.AddSeconds(2);
            Assert.True(delivery.ManualRetryReset(retryAt));
            Assert.Equal(DeliveryStatus.Pending, delivery.Status);
            Assert.Equal(0, delivery.AttemptCount);
            Assert.Equal(retryAt, delivery.NextAttemptAt);
            Assert.False(delivery.ManualRetryReset(retryAt.AddSeconds(1)));
            Assert.Equal(retryAt, delivery.NextAttemptAt);
        }

        [Fact]
        public void ManualRequeue_From_Dead_Reactivates_Preserving_AttemptCount()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now);
            delivery.MarkDead("fatal", now.AddSeconds(1));

            var changed = delivery.ManualRequeue(now.AddSeconds(2));

            Assert.True(changed);
            Assert.Equal(DeliveryStatus.Failed, delivery.Status);
            Assert.Equal(1, delivery.AttemptCount);
            Assert.NotNull(delivery.NextAttemptAt);
        }

        [Fact]
        public void ManualRetryReset_When_Already_Active_Is_NoOp()
        {
            var delivery = NewPending(out var now);

            var changed = delivery.ManualRetryReset(now.AddSeconds(1));

            Assert.False(changed);
            Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        }

        [Fact]
        public void ManualCancel_From_Dead_Is_Idempotent_NoOp()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now);
            delivery.MarkDead("fatal", now.AddSeconds(1));

            var changed = delivery.ManualCancel("operator cancel", now.AddSeconds(2));

            Assert.False(changed);
            Assert.Equal(DeliveryStatus.Dead, delivery.Status);
            Assert.Equal("fatal", delivery.LastError);
        }

        [Fact]
        public void ManualCancel_From_Delivered_Throws()
        {
            var delivery = NewPending(out var now);
            delivery.MarkInFlight(now);
            delivery.MarkDelivered(now.AddSeconds(1));

            Assert.Throws<InvalidOperationException>(() => delivery.ManualCancel("not allowed", now.AddSeconds(2)));
        }

        private static Delivery NewPending(out DateTimeOffset now)
        {
            now = DateTimeOffset.UtcNow;
            return DeliveryFactory.CreatePending(
                Guid.NewGuid(), Guid.NewGuid(), "cust-1", "orders.created", "{}", NewDestination(), now);
        }
    }
}

using System;
using System.Collections.Generic;
using KairoServer.Domain.Deliveries;

namespace KairoServer.UnitTests.Deliveries
{
    public class DeliveryAttemptTests
    {
        [Fact]
        public void Create_Populates_Fields()
        {
            var deliveryId = Guid.NewGuid();
            var startedAt = DateTimeOffset.UtcNow;
            var completedAt = startedAt.AddSeconds(2);
            var metadata = new Dictionary<string, string>
            {
                ["httpStatus"] = "503",
                ["responseBody"] = "temporary error",
            };

            var attempt = DeliveryAttempt.Create(
                deliveryId,
                attemptNumber: 2,
                succeeded: false,
                failureCategory: "Transient",
                error: "request failed",
                metadata,
                startedAt,
                completedAt);

            Assert.Equal(deliveryId, attempt.DeliveryId);
            Assert.Equal(2, attempt.AttemptNumber);
            Assert.False(attempt.Succeeded);
            Assert.Equal("Transient", attempt.FailureCategory);
            Assert.Equal("request failed", attempt.Error);
            Assert.Equal("503", attempt.Metadata["httpStatus"]);
            Assert.Equal(startedAt, attempt.StartedAt);
            Assert.Equal(completedAt, attempt.CompletedAt);
        }

        [Fact]
        public void Create_Throws_On_Empty_DeliveryId()
        {
            var startedAt = DateTimeOffset.UtcNow;
            var completedAt = startedAt.AddSeconds(1);

            Assert.Throws<ArgumentException>(() => DeliveryAttempt.Create(
                Guid.Empty,
                1,
                succeeded: true,
                failureCategory: null,
                error: null,
                metadata: null,
                startedAt,
                completedAt));
        }

        [Fact]
        public void Create_Throws_On_NonPositive_AttemptNumber()
        {
            var startedAt = DateTimeOffset.UtcNow;
            var completedAt = startedAt.AddSeconds(1);

            Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryAttempt.Create(
                Guid.NewGuid(),
                0,
                succeeded: true,
                failureCategory: null,
                error: null,
                metadata: null,
                startedAt,
                completedAt));
        }

        [Fact]
        public void Create_Throws_When_CompletedAt_Precedes_StartedAt()
        {
            var startedAt = DateTimeOffset.UtcNow;

            Assert.Throws<ArgumentException>(() => DeliveryAttempt.Create(
                Guid.NewGuid(),
                1,
                succeeded: true,
                failureCategory: null,
                error: null,
                metadata: null,
                startedAt,
                startedAt.AddSeconds(-1)));
        }

        [Fact]
        public void Create_Truncates_Long_Failure_Error_And_Metadata_Values()
        {
            var longText = new string('x', DeliveryAttempt.MaxCapturedTextLength + 25);
            var startedAt = DateTimeOffset.UtcNow;
            var completedAt = startedAt.AddSeconds(1);

            var attempt = DeliveryAttempt.Create(
                Guid.NewGuid(),
                1,
                succeeded: false,
                failureCategory: longText,
                error: longText,
                metadata: new Dictionary<string, string>
                {
                    ["detail"] = longText,
                },
                startedAt,
                completedAt);

            Assert.Equal(DeliveryAttempt.MaxCapturedTextLength, attempt.FailureCategory!.Length);
            Assert.Equal(DeliveryAttempt.MaxCapturedTextLength, attempt.Error!.Length);
            Assert.Equal(DeliveryAttempt.MaxCapturedTextLength, attempt.Metadata["detail"].Length);
        }
    }
}

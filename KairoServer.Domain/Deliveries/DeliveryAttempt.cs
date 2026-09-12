using System;
using System.Collections.Generic;

namespace KairoServer.Domain.Deliveries
{
    /// <summary>
    /// One recorded attempt at delivering a <see cref="Delivery"/>. Attempts are
    /// append-only: each transition through <see cref="DeliveryStatus.InFlight"/>
    /// produces exactly one row so operators can reconstruct the delivery timeline.
    /// The entity is protocol-neutral: transport-specific detail (HTTP status
    /// code, response snippet, gRPC status, SMTP reply, etc.) is stored in the
    /// opaque <see cref="Metadata"/> bag populated by the destination executor.
    /// </summary>
    public sealed class DeliveryAttempt
    {
        /// <summary>Max characters captured per free-form text field (error, metadata value).</summary>
        public const int MaxCapturedTextLength = 1024;

        public Guid Id { get; }
        public Guid DeliveryId { get; }
        public int AttemptNumber { get; }
        public bool Succeeded { get; }
        public string? FailureCategory { get; }
        public string? Error { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset CompletedAt { get; }

        private DeliveryAttempt(
            Guid id,
            Guid deliveryId,
            int attemptNumber,
            bool succeeded,
            string? failureCategory,
            string? error,
            IReadOnlyDictionary<string, string> metadata,
            DateTimeOffset startedAt,
            DateTimeOffset completedAt)
        {
            Id = id;
            DeliveryId = deliveryId;
            AttemptNumber = attemptNumber;
            Succeeded = succeeded;
            FailureCategory = failureCategory;
            Error = error;
            Metadata = metadata;
            StartedAt = startedAt;
            CompletedAt = completedAt;
        }

        public static DeliveryAttempt Create(
            Guid deliveryId,
            int attemptNumber,
            bool succeeded,
            string? failureCategory,
            string? error,
            IReadOnlyDictionary<string, string>? metadata,
            DateTimeOffset startedAt,
            DateTimeOffset completedAt)
        {
            if (deliveryId == Guid.Empty)
                throw new ArgumentException("DeliveryId cannot be empty.", nameof(deliveryId));
            if (attemptNumber <= 0)
                throw new ArgumentOutOfRangeException(nameof(attemptNumber), "AttemptNumber must be positive.");
            if (completedAt < startedAt)
                throw new ArgumentException("CompletedAt cannot precede StartedAt.", nameof(completedAt));

            return new DeliveryAttempt(
                UuidV7.Create(startedAt),
                deliveryId,
                attemptNumber,
                succeeded,
                Truncate(failureCategory),
                Truncate(error),
                TruncateMetadata(metadata),
                startedAt,
                completedAt);
        }

        public static DeliveryAttempt Rehydrate(
            Guid id,
            Guid deliveryId,
            int attemptNumber,
            bool succeeded,
            string? failureCategory,
            string? error,
            IReadOnlyDictionary<string, string>? metadata,
            DateTimeOffset startedAt,
            DateTimeOffset completedAt) =>
            new(id, deliveryId, attemptNumber, succeeded, failureCategory, error,
                metadata ?? EmptyMetadata, startedAt, completedAt);

        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new Dictionary<string, string>(0);

        private static string? Truncate(string? value)
        {
            if (value is null) return null;
            return value.Length <= MaxCapturedTextLength
                ? value
                : value.Substring(0, MaxCapturedTextLength);
        }

        private static IReadOnlyDictionary<string, string> TruncateMetadata(
            IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata is null || metadata.Count == 0) return EmptyMetadata;

            var copy = new Dictionary<string, string>(metadata.Count);
            foreach (var kvp in metadata)
            {
                copy[kvp.Key] = Truncate(kvp.Value) ?? string.Empty;
            }
            return copy;
        }
    }
}

using System;
using System.Collections.Generic;

namespace NuntivusServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Protocol-neutral outcome returned by an <see cref="IDeliveryExecutor"/>.
    /// Delivery Core inspects <see cref="Succeeded"/> and, on failure, the
    /// <see cref="Category"/> to decide retry vs. terminal disposition.
    /// <see cref="RetryAfter"/> is a hint (e.g. from HTTP <c>Retry-After</c>);
    /// Core is free to ignore it. <see cref="Metadata"/> is opaque and used
    /// only for observability.
    /// </summary>
    public sealed record DeliveryExecutionResult(
        bool Succeeded,
        DeliveryFailureCategory? Category,
        string? Error,
        TimeSpan? RetryAfter,
        IReadOnlyDictionary<string, string>? Metadata)
    {
        public static DeliveryExecutionResult Success(
            IReadOnlyDictionary<string, string>? metadata = null) =>
            new(true, null, null, null, metadata);

        public static DeliveryExecutionResult Failure(
            DeliveryFailureCategory category,
            string error,
            TimeSpan? retryAfter = null,
            IReadOnlyDictionary<string, string>? metadata = null) =>
            new(false, category, error, retryAfter, metadata);
    }
}

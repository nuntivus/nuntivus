using System;
using System.Collections.Generic;

namespace NuntivusServer.Application.Deliveries.Recovery
{
    public sealed record DeliveryAttemptView(
        Guid Id,
        int AttemptNumber,
        bool Succeeded,
        string? FailureCategory,
        string? Error,
        IReadOnlyDictionary<string, string> Metadata,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        long DurationMs);
}

using System;

namespace KairoServer.Application.Deliveries.Queries
{
    public sealed record DeliveryLatestFailureView(
        string? FailureCategory,
        string? Error,
        DateTimeOffset? FailedAt);
}

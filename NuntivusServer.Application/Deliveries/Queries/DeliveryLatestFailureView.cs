using System;

namespace NuntivusServer.Application.Deliveries.Queries
{
    public sealed record DeliveryLatestFailureView(
        string? FailureCategory,
        string? Error,
        DateTimeOffset? FailedAt);
}

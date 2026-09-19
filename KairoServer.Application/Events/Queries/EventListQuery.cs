using System;

namespace KairoServer.Application.Events.Queries
{
    public sealed record EventListQuery(
        string? Type,
        string? Source,
        DateTimeOffset? OccurredFrom,
        DateTimeOffset? OccurredTo,
        DateTimeOffset? ReceivedFrom,
        DateTimeOffset? ReceivedTo,
        string? SortBy,
        string? SortDirection,
        int Page,
        int PageSize);
}

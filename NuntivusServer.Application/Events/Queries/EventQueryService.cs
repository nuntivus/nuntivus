using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Events.Queries
{
    public sealed class EventQueryService : IEventQueryService
    {
        public const int MaxPageSize = 100;

        private readonly IEventRepository _events;

        public EventQueryService(IEventRepository events)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public async Task<Result<EventQueryView>> GetByIdAsync(
            Guid eventId,
            CancellationToken cancellationToken)
        {
            if (eventId == Guid.Empty)
                return Result.Failure<EventQueryView>(Error.Validation("eventId is required."));

            var lookup = await _events.GetQueryByIdAsync(eventId, cancellationToken);
            if (lookup.IsFailure)
                return Result<EventQueryView>.Fail(lookup.Errors);

            if (lookup.Value is null)
                return Result.Failure<EventQueryView>(Error.NotFound($"Event '{eventId}' was not found."));

            return Result.Success(lookup.Value);
        }

        public async Task<Result<PagedResult<EventQueryView>>> ListAsync(
            EventListQuery query,
            CancellationToken cancellationToken)
        {
            if (query is null)
                return Result.Failure<PagedResult<EventQueryView>>(Error.Validation("query is required."));
            if (query.Page < 1)
                return Result.Failure<PagedResult<EventQueryView>>(Error.Validation("page must be greater than or equal to 1."));
            if (query.PageSize < 1 || query.PageSize > MaxPageSize)
            {
                return Result.Failure<PagedResult<EventQueryView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));
            }

            if (query.OccurredFrom.HasValue && query.OccurredTo.HasValue && query.OccurredFrom > query.OccurredTo)
            {
                return Result.Failure<PagedResult<EventQueryView>>(
                    Error.Validation("occurredFrom must be less than or equal to occurredTo."));
            }

            if (query.ReceivedFrom.HasValue && query.ReceivedTo.HasValue && query.ReceivedFrom > query.ReceivedTo)
            {
                return Result.Failure<PagedResult<EventQueryView>>(
                    Error.Validation("receivedFrom must be less than or equal to receivedTo."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortBy)
                && !string.Equals(query.SortBy, "type", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "source", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "occurredAt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "receivedAt", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<EventQueryView>>(
                    Error.Validation("sortBy must be one of: type, source, occurredAt, receivedAt."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortDirection)
                && !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<EventQueryView>>(
                    Error.Validation("sortDirection must be one of: asc, desc."));
            }

            return await _events.ListQueryAsync(query, cancellationToken);
        }
    }
}

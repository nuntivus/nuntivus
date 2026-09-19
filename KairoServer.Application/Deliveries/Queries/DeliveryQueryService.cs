using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;

namespace KairoServer.Application.Deliveries.Queries
{
    public sealed class DeliveryQueryService : IDeliveryQueryService
    {
        public const int MaxPageSize = 100;

        private readonly IDeliveryRepository _deliveries;

        public DeliveryQueryService(IDeliveryRepository deliveries)
        {
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
        }

        public async Task<Result<DeliveryQueryView>> GetByIdAsync(
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            if (deliveryId == Guid.Empty)
                return Result.Failure<DeliveryQueryView>(Error.Validation("deliveryId is required."));

            var lookup = await _deliveries.GetQueryByIdAsync(deliveryId, cancellationToken);
            if (lookup.IsFailure)
                return Result<DeliveryQueryView>.Fail(lookup.Errors);

            if (lookup.Value is null)
                return Result.Failure<DeliveryQueryView>(Error.NotFound($"Delivery '{deliveryId}' was not found."));

            return Result.Success(lookup.Value);
        }

        public async Task<Result<PagedResult<DeliveryQueryView>>> ListAsync(
            DeliveryListQuery query,
            CancellationToken cancellationToken)
        {
            if (query is null)
                return Result.Failure<PagedResult<DeliveryQueryView>>(Error.Validation("query is required."));
            if (query.Page < 1)
                return Result.Failure<PagedResult<DeliveryQueryView>>(Error.Validation("page must be greater than or equal to 1."));
            if (query.PageSize < 1 || query.PageSize > MaxPageSize)
            {
                return Result.Failure<PagedResult<DeliveryQueryView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));
            }

            if (query.CreatedFrom.HasValue && query.CreatedTo.HasValue && query.CreatedFrom > query.CreatedTo)
            {
                return Result.Failure<PagedResult<DeliveryQueryView>>(
                    Error.Validation("createdFrom must be less than or equal to createdTo."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortBy)
                && !string.Equals(query.SortBy, "createdAt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "updatedAt", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<DeliveryQueryView>>(
                    Error.Validation("sortBy must be one of: createdAt, updatedAt."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortDirection)
                && !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<DeliveryQueryView>>(
                    Error.Validation("sortDirection must be one of: asc, desc."));
            }

            return await _deliveries.ListQueryAsync(query, cancellationToken);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Deliveries.Queries;
using KairoServer.Common;
using KairoServer.Contracts.Dashboard;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Dashboard
{
    public sealed class AdminFailedDeliveryQueryService : IAdminFailedDeliveryQueryService
    {
        private readonly IDeliveryQueryService _deliveries;

        public AdminFailedDeliveryQueryService(IDeliveryQueryService deliveries)
        {
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
        }

        public async Task<Result<PagedResult<FailedDeliveryView>>> ListAsync(
            AdminFailedDeliveryListQuery query,
            CancellationToken cancellationToken)
        {
            if (query is null)
                return Result.Failure<PagedResult<FailedDeliveryView>>(Error.Validation("query is required."));

            var deliveryResult = await _deliveries.ListAsync(
                new DeliveryListQuery(
                    query.EventId,
                    query.CustomerId,
                    query.ContractId,
                    DeliveryStatus.Failed,
                    query.CreatedFrom,
                    query.CreatedTo,
                    query.SortBy,
                    query.SortDirection,
                    query.Page,
                    query.PageSize),
                cancellationToken);

            if (deliveryResult.IsFailure)
                return Result<PagedResult<FailedDeliveryView>>.Fail(deliveryResult.Errors);

            var mapped = deliveryResult.Value.Map(delivery => new FailedDeliveryView(
                delivery.Id,
                delivery.EventId,
                delivery.ContractId,
                delivery.CustomerId,
                delivery.EventType,
                delivery.AttemptCount,
                delivery.LastError,
                delivery.NextAttemptAt,
                delivery.CreatedAt,
                delivery.UpdatedAt));

            return Result.Success(mapped);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;

namespace KairoServer.Application.Deliveries.Queries
{
    public interface IDeliveryQueryService
    {
        Task<Result<DeliveryQueryView>> GetByIdAsync(
            Guid deliveryId,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<DeliveryQueryView>>> ListAsync(
            DeliveryListQuery query,
            CancellationToken cancellationToken);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Deliveries.Queries
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

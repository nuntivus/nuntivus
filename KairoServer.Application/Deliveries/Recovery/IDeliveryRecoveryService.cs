using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;

namespace KairoServer.Application.Deliveries.Recovery
{
    public interface IDeliveryRecoveryService
    {
        Task<Result<PagedResult<DeliveryRecoveryView>>> ListDeadAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<Result<DeliveryRecoveryView>> ManualRetryAsync(
            Guid deliveryId,
            CancellationToken cancellationToken);

        Task<Result<DeliveryRecoveryView>> ManualRequeueAsync(
            Guid deliveryId,
            CancellationToken cancellationToken);
    }
}

using System;
using System.Collections.Generic;
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
            ManualDeliveryOperationRequest request,
            CancellationToken cancellationToken);

        Task<Result<DeliveryRecoveryView>> ManualRequeueAsync(
            Guid deliveryId,
            ManualDeliveryOperationRequest request,
            CancellationToken cancellationToken);

        Task<Result<DeliveryRecoveryView>> ManualCancelAsync(
            Guid deliveryId,
            ManualDeliveryOperationRequest request,
            CancellationToken cancellationToken);

        Task<Result<DeliveryFailureView>> GetFailureAsync(
            Guid deliveryId,
            CancellationToken cancellationToken);

        Task<Result<IReadOnlyList<DeliveryAttemptView>>> GetAttemptsAsync(
            Guid deliveryId,
            CancellationToken cancellationToken);
    }
}

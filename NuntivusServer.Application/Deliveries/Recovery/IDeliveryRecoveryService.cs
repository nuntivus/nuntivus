using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Deliveries.Recovery
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

        Task<Result<DeliveryReplayJobView>> SubmitReplayJobAsync(
            DeliveryReplayRequest request,
            CancellationToken cancellationToken);

        Task<Result<DeliveryReplayJobView>> GetReplayJobAsync(
            Guid replayJobId,
            CancellationToken cancellationToken);

        Task<Result<DeliveryReplayWorkerResult>> ProcessReplayJobsAsync(
            int maxJobs,
            CancellationToken cancellationToken);
    }
}

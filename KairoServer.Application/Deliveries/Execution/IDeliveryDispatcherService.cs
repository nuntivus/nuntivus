using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;

namespace KairoServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Application-level orchestrator that turns a persisted Delivery id into a
    /// single dispatch attempt: load Delivery, invoke the destination executor,
    /// persist the resulting <see cref="Domain.Deliveries.DeliveryAttempt"/> and
    /// Delivery transition. Consumed by the delivery worker.
    /// </summary>
    public interface IDeliveryDispatcherService
    {
        Task<Result<DeliveryDispatchOutcome>> DispatchAsync(
            Guid deliveryId,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Outcome of a single dispatch. The worker uses this to decide whether to
    /// Ack (success/terminal/skip) or Nak (transient persistence failure).
    /// </summary>
    public enum DeliveryDispatchOutcome
    {
        Delivered = 0,
        FailedRetry = 1,
        FailedTerminal = 2,
        AlreadyTerminal = 3,
        Skipped = 4,
        NotFound = 5,
    }
}

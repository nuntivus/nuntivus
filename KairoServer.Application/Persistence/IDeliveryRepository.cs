using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Persistence
{
    public interface IDeliveryRepository
    {
        /// <summary>
        /// Idempotently inserts a batch of <see cref="Delivery"/> rows in status
        /// <see cref="DeliveryStatus.Pending"/>. Duplicates (same event_id +
        /// contract_id) are silently ignored via <c>ON CONFLICT DO NOTHING</c>,
        /// making at-least-once redelivery safe.
        /// </summary>
        /// <returns>The number of rows actually inserted (excluding duplicates).</returns>
        Task<Result<int>> InsertPendingAsync(
            IReadOnlyCollection<Delivery> deliveries,
            IUnitOfWork uow,
            CancellationToken cancellationToken);
    }
}

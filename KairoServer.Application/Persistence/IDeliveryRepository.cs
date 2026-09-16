using System;
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

        /// <summary>
        /// Loads a <see cref="Delivery"/> by id. Returns <c>null</c> when the
        /// row does not exist.
        /// </summary>
        Task<Result<Delivery?>> GetByIdAsync(
            Guid deliveryId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Persists the current state of a <see cref="Delivery"/> back to the
        /// store. Uses the row's <c>updated_at</c> as an optimistic guard so
        /// concurrent dispatchers cannot clobber each other's transitions.
        /// </summary>
        Task<Result<bool>> UpdateAsync(
            Delivery delivery,
            DateTimeOffset expectedUpdatedAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        /// <summary>
        /// Appends a <see cref="DeliveryAttempt"/> row. Duplicate
        /// (delivery_id, attempt_number) pairs are ignored so retries of the
        /// same in-flight dispatch stay idempotent.
        /// </summary>
        Task<Result<int>> AddAttemptAsync(
            DeliveryAttempt attempt,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        /// <summary>
        /// Loads all recorded attempts for a delivery ordered by attempt number.
        /// </summary>
        Task<Result<IReadOnlyList<DeliveryAttempt>>> GetAttemptsByDeliveryIdAsync(
            Guid deliveryId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Lists terminal dead deliveries ordered by most recently updated.
        /// </summary>
        Task<Result<PagedResult<Delivery>>> ListDeadAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<Result<bool>> ScheduleNextAttemptAsync(
            Guid deliveryId,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset nextAttemptAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken);
    }
}


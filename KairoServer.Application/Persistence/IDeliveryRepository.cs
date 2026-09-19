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
        /// Atomically claims a delivery for active execution by transitioning the
        /// row to <see cref="DeliveryStatus.InFlight"/> and incrementing
        /// <c>attempt_count</c>. Returns the claimed row, or <c>null</c> when the
        /// optimistic guard failed (already claimed/updated by another worker) or
        /// the row is not currently eligible.
        /// </summary>
        Task<Result<Delivery?>> TryClaimForDispatchAsync(
            Guid deliveryId,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset now,
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

        /// <summary>
        /// Atomically claims a batch of due delayed deliveries for enqueueing by
        /// the durable scheduler. Rows are selected using
        /// <c>FOR UPDATE SKIP LOCKED</c> so multiple schedulers can scale out
        /// safely. Claimed rows are made dispatchable and returned for enqueue.
        /// </summary>
        Task<Result<IReadOnlyList<Delivery>>> ClaimDueForSchedulingAsync(
            DateTimeOffset now,
            int batchSize,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        /// <summary>
        /// Recovers abandoned in-flight deliveries by moving rows whose
        /// <c>updated_at</c> is older than <paramref name="staleBefore"/> back to
        /// retryable <see cref="DeliveryStatus.Failed"/> with
        /// <c>next_attempt_at = now</c>.
        /// </summary>
        Task<Result<int>> RecoverStaleInFlightAsync(
            DateTimeOffset staleBefore,
            DateTimeOffset now,
            int batchSize,
            CancellationToken cancellationToken);
    }
}


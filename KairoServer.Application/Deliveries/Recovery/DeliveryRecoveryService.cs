using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Deliveries.Recovery
{
    public sealed class DeliveryRecoveryService : IDeliveryRecoveryService
    {
        public const int MaxPageSize = 100;

        private readonly IDeliveryRepository _deliveries;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<DeliveryRecoveryService> _logger;

        public DeliveryRecoveryService(
            IDeliveryRepository deliveries,
            IUnitOfWork unitOfWork,
            IAppLogger<DeliveryRecoveryService> logger)
        {
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<PagedResult<DeliveryRecoveryView>>> ListDeadAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (page < 1)
                return Result.Failure<PagedResult<DeliveryRecoveryView>>(Error.Validation("page must be greater than or equal to 1."));
            if (pageSize < 1 || pageSize > MaxPageSize)
                return Result.Failure<PagedResult<DeliveryRecoveryView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));

            var list = await _deliveries.ListDeadAsync(page, pageSize, cancellationToken);
            if (list.IsFailure)
                return Result<PagedResult<DeliveryRecoveryView>>.Fail(list.Errors);

            return Result.Success(list.Value.Map(ToView));
        }

        public Task<Result<DeliveryRecoveryView>> ManualRetryAsync(
            Guid deliveryId,
            CancellationToken cancellationToken) =>
            RecoverAsync(deliveryId, retryReset: true, cancellationToken);

        public Task<Result<DeliveryRecoveryView>> ManualRequeueAsync(
            Guid deliveryId,
            CancellationToken cancellationToken) =>
            RecoverAsync(deliveryId, retryReset: false, cancellationToken);

        private async Task<Result<DeliveryRecoveryView>> RecoverAsync(
            Guid deliveryId,
            bool retryReset,
            CancellationToken cancellationToken)
        {
            if (deliveryId == Guid.Empty)
                return Result.Failure<DeliveryRecoveryView>(Error.Validation("deliveryId is required."));

            try
            {
                while (true)
                {
                    var lookup = await _deliveries.GetByIdAsync(deliveryId, cancellationToken);
                    if (lookup.IsFailure)
                        return Result<DeliveryRecoveryView>.Fail(lookup.Errors);

                    var delivery = lookup.Value;
                    if (delivery is null)
                        return Result.Failure<DeliveryRecoveryView>(Error.NotFound($"Delivery '{deliveryId}' was not found."));

                    var expectedUpdatedAt = delivery.UpdatedAt;
                    var now = DateTimeOffset.UtcNow;
                    var changed = retryReset
                        ? delivery.ManualRetryReset(now)
                        : delivery.ManualRequeue(now);

                    if (!changed)
                    {
                        return Result.Success(ToView(delivery));
                    }

                    _unitOfWork.Begin(true);
                    var update = await _deliveries.UpdateAsync(
                        delivery,
                        expectedUpdatedAt,
                        _unitOfWork,
                        cancellationToken);
                    if (update.IsFailure)
                    {
                        _unitOfWork.Rollback();
                        return Result<DeliveryRecoveryView>.Fail(update.Errors);
                    }

                    if (update.Value)
                    {
                        _unitOfWork.Commit();
                        _logger.Information(
                            "Manual delivery recovery applied ({Mode}) for {DeliveryId}.",
                            retryReset ? "retry-reset" : "requeue",
                            deliveryId);
                        return Result.Success(ToView(delivery));
                    }

                    _unitOfWork.Rollback();
                }
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed manual delivery recovery ({Mode}) for {DeliveryId}.",
                    retryReset ? "retry-reset" : "requeue", deliveryId);
                return Result.Failure<DeliveryRecoveryView>(ex);
            }
        }

        private static DeliveryRecoveryView ToView(Delivery delivery) =>
            new(
                delivery.Id,
                delivery.Status,
                delivery.AttemptCount,
                delivery.LastError,
                delivery.NextAttemptAt,
                delivery.UpdatedAt);
    }
}

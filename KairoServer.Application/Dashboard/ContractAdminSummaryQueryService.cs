using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Contracts;
using KairoServer.Application.Deliveries.Health;
using KairoServer.Application.Deliveries.Queries;
using KairoServer.Common;
using KairoServer.Contracts.Dashboard;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Dashboard
{
    public sealed class ContractAdminSummaryQueryService : IContractAdminSummaryQueryService
    {
        private readonly IContractService _contracts;
        private readonly IDeliveryQueryService _deliveries;
        private readonly IDestinationHealthQueryService _destinationHealth;

        public ContractAdminSummaryQueryService(
            IContractService contracts,
            IDeliveryQueryService deliveries,
            IDestinationHealthQueryService destinationHealth)
        {
            _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
            _destinationHealth = destinationHealth ?? throw new ArgumentNullException(nameof(destinationHealth));
        }

        public async Task<Result<ContractAdminSummaryView>> GetAsync(Guid contractId, CancellationToken cancellationToken)
        {
            if (contractId == Guid.Empty)
                return Result.Failure<ContractAdminSummaryView>(Error.Validation("contractId is required."));

            var contractResult = await _contracts.GetByIdAsync(contractId, cancellationToken);
            if (contractResult.IsFailure)
                return Result<ContractAdminSummaryView>.Fail(contractResult.Errors);

            var pendingResult = await CountByStatusAsync(contractId, DeliveryStatus.Pending, cancellationToken);
            if (pendingResult.IsFailure)
                return Result<ContractAdminSummaryView>.Fail(pendingResult.Errors);

            var inFlightResult = await CountByStatusAsync(contractId, DeliveryStatus.InFlight, cancellationToken);
            if (inFlightResult.IsFailure)
                return Result<ContractAdminSummaryView>.Fail(inFlightResult.Errors);

            var deliveredResult = await CountByStatusAsync(contractId, DeliveryStatus.Delivered, cancellationToken);
            if (deliveredResult.IsFailure)
                return Result<ContractAdminSummaryView>.Fail(deliveredResult.Errors);

            var failedResult = await CountByStatusAsync(contractId, DeliveryStatus.Failed, cancellationToken);
            if (failedResult.IsFailure)
                return Result<ContractAdminSummaryView>.Fail(failedResult.Errors);

            var deadResult = await CountByStatusAsync(contractId, DeliveryStatus.Dead, cancellationToken);
            if (deadResult.IsFailure)
                return Result<ContractAdminSummaryView>.Fail(deadResult.Errors);

            ContractDestinationHealthSnapshotView? healthSnapshot = null;
            var healthResult = await _destinationHealth.GetByContractIdAsync(contractId, cancellationToken);
            if (healthResult.IsSuccess)
            {
                var health = healthResult.Value;
                healthSnapshot = new ContractDestinationHealthSnapshotView(
                    health.DestinationType,
                    health.ConsecutiveFailures,
                    health.LastSuccessAt,
                    health.LastFailureAt,
                    health.LastAttemptAt,
                    health.Status.ToString(),
                    health.UpdatedAt);
            }
            else if (healthResult.Error?.Code != "not_found")
            {
                return Result<ContractAdminSummaryView>.Fail(healthResult.Errors);
            }

            var contract = contractResult.Value;
            return Result.Success(new ContractAdminSummaryView(
                contract.Id,
                contract.CustomerId,
                contract.Name,
                contract.Enabled,
                contract.DestinationType,
                contract.EventTypes.Count,
                pendingResult.Value,
                inFlightResult.Value,
                deliveredResult.Value,
                failedResult.Value,
                deadResult.Value,
                healthSnapshot,
                DateTimeOffset.UtcNow));
        }

        private async Task<Result<long>> CountByStatusAsync(
            Guid contractId,
            DeliveryStatus status,
            CancellationToken cancellationToken)
        {
            var result = await _deliveries.ListAsync(
                new DeliveryListQuery(
                    null,
                    null,
                    contractId,
                    status,
                    null,
                    null,
                    null,
                    null,
                    1,
                    1),
                cancellationToken);

            if (result.IsFailure)
                return Result<long>.Fail(result.Errors);

            return Result.Success(result.Value.TotalCount);
        }
    }
}

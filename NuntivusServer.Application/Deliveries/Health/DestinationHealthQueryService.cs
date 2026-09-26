using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Deliveries.Health
{
    public sealed class DestinationHealthQueryService : IDestinationHealthQueryService
    {
        private readonly IDeliveryRepository _deliveries;

        public DestinationHealthQueryService(IDeliveryRepository deliveries)
        {
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
        }

        public async Task<Result<DestinationHealthView>> GetByContractIdAsync(
            Guid contractId,
            CancellationToken cancellationToken)
        {
            if (contractId == Guid.Empty)
                return Result.Failure<DestinationHealthView>(Error.Validation("contractId is required."));

            var result = await _deliveries.GetDestinationHealthByContractIdAsync(contractId, cancellationToken);
            if (result.IsFailure)
                return Result<DestinationHealthView>.Fail(result.Errors);

            if (result.Value is null)
                return Result.Failure<DestinationHealthView>(Error.NotFound($"Destination health for contract '{contractId}' was not found."));

            return Result.Success(result.Value);
        }

        public async Task<Result<IReadOnlyList<DestinationHealthView>>> ListByCustomerIdAsync(
            string customerId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(customerId))
                return Result.Failure<IReadOnlyList<DestinationHealthView>>(Error.Validation("customerId is required."));

            return await _deliveries.ListDestinationHealthByCustomerIdAsync(customerId.Trim(), cancellationToken);
        }
    }
}

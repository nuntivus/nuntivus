using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Deliveries.Health
{
    public interface IDestinationHealthQueryService
    {
        Task<Result<DestinationHealthView>> GetByContractIdAsync(
            Guid contractId,
            CancellationToken cancellationToken);

        Task<Result<IReadOnlyList<DestinationHealthView>>> ListByCustomerIdAsync(
            string customerId,
            CancellationToken cancellationToken);
    }
}

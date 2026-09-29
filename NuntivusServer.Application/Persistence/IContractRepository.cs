using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Persistence
{
    public interface IContractRepository
    {
        Task<Result> InsertAsync(
            Contract contract,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result> UpdateAsync(
            Contract contract,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result<Contract?>> GetByIdAsync(
            System.Guid id,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<Contract>>> ListByCustomerAsync(
            ContractListQuery query,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<Contract>>> ListAsync(
            AdminContractListQuery query,
            CancellationToken cancellationToken);

        /// <summary>
        /// Returns the ids of all enabled Contracts (with enabled Customers)
        /// whose subscribed event types include <paramref name="eventType"/> and
        /// whose selected Sources include <paramref name="sourceId"/>.
        /// Hot path invoked once per accepted event by the contract matcher.
        /// </summary>
        Task<Result<IReadOnlyList<ContractMatch>>> ListEnabledMatchingAsync(
            string eventType,
            string sourceId,
            CancellationToken cancellationToken);
    }
}

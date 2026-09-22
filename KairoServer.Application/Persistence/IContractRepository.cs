using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Contracts;
using KairoServer.Common;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Persistence
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
        /// whose subscribed event types include <paramref name="eventType"/>.
        /// Hot path invoked once per accepted event by the contract matcher.
        /// </summary>
        Task<Result<IReadOnlyList<ContractMatch>>> ListEnabledMatchingAsync(
            string eventType,
            CancellationToken cancellationToken);
    }
}

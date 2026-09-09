using System.Threading;
using System.Threading.Tasks;
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
            string customerId,
            int page,
            int pageSize,
            CancellationToken cancellationToken);
    }
}

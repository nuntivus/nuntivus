using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Customers;
using KairoServer.Common;
using KairoServer.Domain.Customers;

namespace KairoServer.Application.Persistence
{
    public interface ICustomerRepository
    {
        Task<Result> InsertAsync(
            Customer customer,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result> UpdateAsync(
            Customer customer,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result<Customer?>> GetByIdAsync(
            string id,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<Customer>>> ListAsync(
            CustomerListQuery query,
            CancellationToken cancellationToken);
    }
}

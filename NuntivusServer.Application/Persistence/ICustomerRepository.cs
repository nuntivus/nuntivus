using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Customers;
using NuntivusServer.Common;
using NuntivusServer.Domain.Customers;

namespace NuntivusServer.Application.Persistence
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

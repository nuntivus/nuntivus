using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Customers;

namespace KairoServer.Application.Customers
{
    public interface ICustomerService
    {
        Task<Result<CustomerCreated>> CreateAsync(
            string id,
            string name,
            CancellationToken cancellationToken);

        Task<Result<CustomerView>> GetByIdAsync(
            string id,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<CustomerView>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<Result<CustomerView>> EnableAsync(
            string id,
            CancellationToken cancellationToken);

        Task<Result<CustomerView>> DisableAsync(
            string id,
            CancellationToken cancellationToken);
    }
}

using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Customers;

namespace NuntivusServer.Application.Customers
{
    public interface ICustomerService
    {
        Task<Result<CustomerCreated>> CreateAsync(
            string id,
            string name,
            CancellationToken cancellationToken,
            string? actorId = null);

        Task<Result<CustomerView>> GetByIdAsync(
            string id,
            CancellationToken cancellationToken);

        Task<Result<CustomerView>> UpdateAsync(
            string id,
            string name,
            CancellationToken cancellationToken,
            string? actorId = null);

        Task<Result<PagedResult<CustomerView>>> ListAsync(
            CustomerListQuery query,
            CancellationToken cancellationToken);

        Task<Result<CustomerView>> EnableAsync(
            string id,
            CancellationToken cancellationToken,
            string? actorId = null);

        Task<Result<CustomerView>> DisableAsync(
            string id,
            CancellationToken cancellationToken,
            string? actorId = null);
    }
}

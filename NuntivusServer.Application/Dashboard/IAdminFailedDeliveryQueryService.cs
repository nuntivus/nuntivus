using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Dashboard;

namespace NuntivusServer.Application.Dashboard
{
    public interface IAdminFailedDeliveryQueryService
    {
        Task<Result<PagedResult<FailedDeliveryView>>> ListAsync(
            AdminFailedDeliveryListQuery query,
            CancellationToken cancellationToken);
    }
}

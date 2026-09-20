using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Dashboard;

namespace KairoServer.Application.Dashboard
{
    public interface IAdminFailedDeliveryQueryService
    {
        Task<Result<PagedResult<FailedDeliveryView>>> ListAsync(
            AdminFailedDeliveryListQuery query,
            CancellationToken cancellationToken);
    }
}

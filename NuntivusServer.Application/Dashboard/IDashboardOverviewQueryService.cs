using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Dashboard;

namespace NuntivusServer.Application.Dashboard
{
    public interface IDashboardOverviewQueryService
    {
        Task<Result<DashboardOverviewView>> GetAsync(CancellationToken cancellationToken);
    }
}

using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Dashboard;

namespace KairoServer.Application.Dashboard
{
    public interface IDashboardOverviewQueryService
    {
        Task<Result<DashboardOverviewView>> GetAsync(CancellationToken cancellationToken);
    }
}

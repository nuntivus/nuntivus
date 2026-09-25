using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Dashboard;

namespace KairoServer.Application.Dashboard
{
    public interface IDashboardTimeSeriesQueryService
    {
        Task<Result<DashboardTimeSeriesView>> GetAsync(int rangeSeconds, CancellationToken cancellationToken);
    }
}

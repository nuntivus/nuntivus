using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Dashboard;

namespace NuntivusServer.Application.Dashboard
{
    public interface IDashboardTimeSeriesQueryService
    {
        Task<Result<DashboardTimeSeriesView>> GetAsync(int rangeSeconds, CancellationToken cancellationToken);
    }
}

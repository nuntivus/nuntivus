using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Dashboard;

namespace KairoServer.Application.Dashboard
{
    public interface ICustomerAdminSummaryQueryService
    {
        Task<Result<CustomerAdminSummaryView>> GetAsync(string customerId, CancellationToken cancellationToken);
    }
}

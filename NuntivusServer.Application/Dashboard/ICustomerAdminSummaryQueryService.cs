using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Dashboard;

namespace NuntivusServer.Application.Dashboard
{
    public interface ICustomerAdminSummaryQueryService
    {
        Task<Result<CustomerAdminSummaryView>> GetAsync(string customerId, CancellationToken cancellationToken);
    }
}

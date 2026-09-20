using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Dashboard;

namespace KairoServer.Application.Dashboard
{
    public interface IContractAdminSummaryQueryService
    {
        Task<Result<ContractAdminSummaryView>> GetAsync(Guid contractId, CancellationToken cancellationToken);
    }
}

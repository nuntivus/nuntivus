using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Dashboard;

namespace NuntivusServer.Application.Dashboard
{
    public interface IContractAdminSummaryQueryService
    {
        Task<Result<ContractAdminSummaryView>> GetAsync(Guid contractId, CancellationToken cancellationToken);
    }
}

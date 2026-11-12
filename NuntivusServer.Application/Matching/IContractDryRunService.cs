using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Application.Matching
{
    public interface IContractDryRunService
    {
        Task<Result<ContractDryRunReport>> RunAsync(Guid? contractId, ContractDryRunRequest request, CancellationToken cancellationToken);
    }
}

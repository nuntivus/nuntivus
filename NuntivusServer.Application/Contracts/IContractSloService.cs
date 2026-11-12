using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Application.Contracts
{
    public interface IContractSloService
    {
        Task<Result<ContractSloReport>> GetAsync(Guid contractId, CancellationToken cancellationToken);
        Task<Result<ContractSloReport>> ConfigureAsync(Guid contractId, ContractSloPolicyRequest request, string actorId, CancellationToken cancellationToken);
    }
}

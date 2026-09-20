using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;

namespace KairoServer.Application.Contracts
{
    public interface IContractService
    {
        Task<Result<ContractCreated>> CreateAsync(
            CreateContractRequest request,
            CancellationToken cancellationToken,
            string? actorId = null);

        Task<Result<ContractView>> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<ContractView>>> ListByCustomerAsync(
            ContractListQuery query,
            CancellationToken cancellationToken);

        Task<Result<ContractView>> EnableAsync(
            Guid id,
            CancellationToken cancellationToken,
            string? actorId = null);

        Task<Result<ContractView>> DisableAsync(
            Guid id,
            CancellationToken cancellationToken,
            string? actorId = null);

        Task<Result<ContractView>> UpdateCredentialsAsync(
            Guid id,
            UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken,
            string? actorId = null);

        Task<Result<ContractView>> RotateCredentialsAsync(
            Guid id,
            UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken,
            string? actorId = null);
    }
}

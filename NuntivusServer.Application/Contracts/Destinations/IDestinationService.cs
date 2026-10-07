using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Contracts.Destinations
{
    public interface IDestinationService
    {
        Task<Result<DestinationResource>> CreateAsync(string name, string? description, DestinationConfig configuration,
            CancellationToken cancellationToken, string? actorId = null);
        Task<Result<DestinationResource>> UpdateAsync(Guid id, string name, string? description, DestinationConfig configuration,
            CancellationToken cancellationToken, string? actorId = null);
        Task<Result<DestinationResource>> SetEnabledAsync(Guid id, bool enabled, bool pauseExistingBindings,
            CancellationToken cancellationToken, string? actorId = null);
        Task<Result> DeleteAsync(Guid id, bool removeBindings, CancellationToken cancellationToken, string? actorId = null);
        Task<Result<DestinationResource>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
        Task<Result<PagedResult<DestinationResource>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    }
}

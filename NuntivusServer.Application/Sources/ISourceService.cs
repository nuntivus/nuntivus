using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public interface ISourceService
    {
        Task<Result<Source>> CreateAsync(string name, string type, string configuration, string? description,
            CancellationToken cancellationToken, string? actorId = null);
        Task<Result<Source>> GetByIdAsync(string id, CancellationToken cancellationToken);
        Task<Result<PagedResult<Source>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
        Task<Result<Source>> UpdateAsync(string id, string name, string configuration, string? description,
            CancellationToken cancellationToken, string? actorId = null);
        Task<Result<Source>> EnableAsync(string id, CancellationToken cancellationToken, string? actorId = null);
        Task<Result<Source>> DisableAsync(string id, CancellationToken cancellationToken, string? actorId = null);
        Task<Result> DeleteAsync(string id, CancellationToken cancellationToken, string? actorId = null);
    }
}

using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Persistence
{
    public interface ISourceRepository
    {
        Task<Result> InsertAsync(Source source, IUnitOfWork unitOfWork, CancellationToken cancellationToken);
        Task<Result> UpdateAsync(Source source, IUnitOfWork unitOfWork, CancellationToken cancellationToken);
        Task<Result> DeleteAsync(string id, IUnitOfWork unitOfWork, CancellationToken cancellationToken);
        Task<Result<Source?>> GetByIdAsync(string id, CancellationToken cancellationToken);
        Task<Result<PagedResult<Source>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
        Task<Result<IReadOnlyList<Source>>> ListEnabledAsync(CancellationToken cancellationToken);
    }
}

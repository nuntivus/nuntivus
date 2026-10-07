using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Persistence
{
    public interface IDestinationRepository
    {
        Task<Result<DestinationResource?>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
        Task<Result<PagedResult<DestinationResource>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
        Task<Result> InsertAsync(DestinationResource destination, IUnitOfWork unitOfWork, CancellationToken cancellationToken);
        Task<Result> UpdateAsync(DestinationResource destination, IUnitOfWork unitOfWork, CancellationToken cancellationToken);
        Task<Result> DeleteAsync(Guid id, bool removeBindings, IUnitOfWork unitOfWork, CancellationToken cancellationToken);
    }
}

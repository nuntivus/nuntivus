using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Auditing;
using KairoServer.Common;
using KairoServer.Domain.Auditing;

namespace KairoServer.Application.Persistence
{
    public interface IAuditLogRepository
    {
        Task<Result<int>> AddAsync(
            AuditLogEntry entry,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<AuditLogListItemView>>> ListAsync(
            AuditLogListQuery query,
            CancellationToken cancellationToken);
    }
}

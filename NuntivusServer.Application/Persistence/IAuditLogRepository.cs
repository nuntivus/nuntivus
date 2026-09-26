using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Auditing;
using NuntivusServer.Common;
using NuntivusServer.Domain.Auditing;

namespace NuntivusServer.Application.Persistence
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

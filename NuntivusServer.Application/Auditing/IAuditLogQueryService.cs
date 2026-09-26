using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Auditing
{
    public interface IAuditLogQueryService
    {
        Task<Result<PagedResult<AuditLogListItemView>>> ListAsync(
            AuditLogListQuery query,
            CancellationToken cancellationToken);
    }
}
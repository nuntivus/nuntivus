using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Events.Queries
{
    public interface IEventQueryService
    {
        Task<Result<EventQueryView>> GetByIdAsync(
            Guid eventId,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<EventQueryView>>> ListAsync(
            EventListQuery query,
            CancellationToken cancellationToken);
    }
}

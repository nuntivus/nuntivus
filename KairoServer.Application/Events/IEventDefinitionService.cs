using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Events;

namespace KairoServer.Application.Events
{
    public interface IEventDefinitionService
    {
        Task<Result<EventDefinitionCreated>> CreateAsync(
            string eventType,
            string displayName,
            string? description,
            CancellationToken cancellationToken);

        Task<Result<EventDefinitionView>> GetByEventTypeAsync(
            string eventType,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<EventDefinitionView>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken);
    }
}
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Domain.Events;

namespace KairoServer.Application.Persistence
{
    public interface IEventDefinitionRepository
    {
        Task<Result> InsertAsync(
            EventDefinition definition,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result<EventDefinition?>> GetByEventTypeAsync(
            string eventType,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<EventDefinition>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken);
    }
}
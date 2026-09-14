using KairoServer.Common;
using KairoServer.Domain.Events;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Application.Persistence
{
    public interface IEventRepository
    {
        Task<Result> InsertAsync(
            Event @event,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        /// <summary>
        /// Loads an <see cref="Event"/> by id from the authoritative store,
        /// or returns a successful <c>null</c> result when it does not exist.
        /// </summary>
        Task<Result<Event?>> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken);

        Task<Result<EventReplay?>> GetReplayAsync(
            Guid sourceEventId,
            string replayKey,
            CancellationToken cancellationToken);

        Task<Result> InsertReplayAsync(
            EventReplay replay,
            IUnitOfWork uow,
            CancellationToken cancellationToken);
    }
}

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
    }
}

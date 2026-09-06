using KairoServer.Common;
using KairoServer.Domain.Outbox;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Application.Persistence
{
    public interface IOutboxRepository
    {
        Task<Result> InsertAsync(
            OutboxMessage message,
            IUnitOfWork uow,
            CancellationToken cancellationToken);
    }
}

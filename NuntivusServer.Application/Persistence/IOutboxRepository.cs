using NuntivusServer.Common;
using NuntivusServer.Domain.Outbox;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuntivusServer.Application.Persistence
{
    public interface IOutboxRepository
    {
        Task<Result> InsertAsync(
            OutboxMessage message,
            IUnitOfWork uow,
            CancellationToken cancellationToken);
    }
}

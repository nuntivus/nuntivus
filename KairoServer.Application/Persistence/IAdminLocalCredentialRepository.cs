using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;

namespace KairoServer.Application.Persistence
{
    public interface IAdminLocalCredentialRepository
    {
        Task<Result<AdminLocalCredential?>> GetBySubjectAsync(
            string subject,
            CancellationToken cancellationToken);

        Task<Result> UpsertAsync(
            string subject,
            string passwordHash,
            DateTimeOffset now,
            IUnitOfWork uow,
            CancellationToken cancellationToken);
    }
}

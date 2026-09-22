using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Domain.Security;

namespace KairoServer.Application.Persistence
{
    public interface IAdminUserRoleRepository
    {
        Task<Result> ReplaceAsync(
            string subject,
            string? displayName,
            IReadOnlyList<string> roles,
            DateTimeOffset now,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result<AdminUserRoleAssignment?>> GetBySubjectAsync(
            string subject,
            CancellationToken cancellationToken);

        Task<Result<bool>> DeleteBySubjectAsync(
            string subject,
            IUnitOfWork uow,
            CancellationToken cancellationToken);
    }
}
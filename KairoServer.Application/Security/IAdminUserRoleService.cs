using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Admin;

namespace KairoServer.Application.Security
{
    public interface IAdminUserRoleService
    {
        Task<Result<AdminUserRoleAssignmentView>> ReplaceAsync(
            string subject,
            string? displayName,
            IReadOnlyList<string> roles,
            string? actorId,
            CancellationToken cancellationToken);

        Task<Result> DeleteAsync(
            string subject,
            string? actorId,
            CancellationToken cancellationToken);
    }
}
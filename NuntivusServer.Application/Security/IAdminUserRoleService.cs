using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Admin;

namespace NuntivusServer.Application.Security
{
    public interface IAdminUserRoleService
    {
        Task<Result<IReadOnlyList<AdminUserRoleAssignmentView>>> ListAsync(
            CancellationToken cancellationToken);

        Task<Result<AdminUserRoleAssignmentView>> GetAsync(
            string subject,
            CancellationToken cancellationToken);

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
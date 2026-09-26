using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Security
{
    public interface IAdminLocalCredentialService
    {
        Task<Result> SetPasswordAsync(
            string subject,
            string password,
            string? actorId,
            CancellationToken cancellationToken);
    }
}

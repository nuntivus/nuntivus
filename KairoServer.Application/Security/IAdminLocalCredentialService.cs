using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;

namespace KairoServer.Application.Security
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

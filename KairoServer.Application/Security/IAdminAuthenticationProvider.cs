using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;

namespace KairoServer.Application.Security
{
    public interface IAdminAuthenticationProvider
    {
        Task<Result<AdminAuthenticatedIdentity>> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken);
    }
}

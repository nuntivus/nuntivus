using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Security
{
    public interface IAdminAuthenticationProvider
    {
        Task<Result<AdminAuthenticatedIdentity>> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken);
    }
}

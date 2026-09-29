using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public interface IHttpSourceAuthenticator
    {
        Task<Result> AuthenticateAsync(Source source, string? token, CancellationToken cancellationToken);
    }
}

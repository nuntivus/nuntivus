using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public interface ISourceConnectionTester
    {
        SourceType Type { get; }
        Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken);
    }
}

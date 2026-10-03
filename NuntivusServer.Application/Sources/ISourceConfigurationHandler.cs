using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public interface ISourceConfigurationHandler
    {
        SourceType Type { get; }
        Result<string> Normalize(string configuration);
    }
}

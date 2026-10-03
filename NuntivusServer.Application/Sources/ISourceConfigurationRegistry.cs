using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public interface ISourceConfigurationRegistry
    {
        bool IsOperational(SourceType type);

        Result<string> Normalize(SourceType type, string configuration);
    }
}

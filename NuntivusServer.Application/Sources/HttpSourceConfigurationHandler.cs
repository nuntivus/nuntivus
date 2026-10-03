using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed class HttpSourceConfigurationHandler : ISourceConfigurationHandler
    {
        public SourceType Type => SourceType.HTTP_API;

        public Result<string> Normalize(string configuration) => HttpSourceConfigurationValidator.Normalize(configuration);
    }
}

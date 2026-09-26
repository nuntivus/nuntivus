using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public interface ISourceAdapterRegistry
    {
        Result<ISourceAdapter> Get(SourceType sourceType);
    }
}

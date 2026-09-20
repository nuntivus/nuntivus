using KairoServer.Common;
using KairoServer.Domain.Sources;

namespace KairoServer.Application.Sources
{
    public interface ISourceAdapterRegistry
    {
        Result<ISourceAdapter> Get(SourceType sourceType);
    }
}

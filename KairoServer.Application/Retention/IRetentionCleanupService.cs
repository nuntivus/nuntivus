using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;

namespace KairoServer.Application.Retention
{
    public interface IRetentionCleanupService
    {
        Task<Result<RetentionCleanupResult>> RunCleanupCycleAsync(CancellationToken cancellationToken);
    }
}
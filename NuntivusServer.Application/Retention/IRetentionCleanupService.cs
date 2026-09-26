using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Retention
{
    public interface IRetentionCleanupService
    {
        Task<Result<RetentionCleanupResult>> RunCleanupCycleAsync(CancellationToken cancellationToken);
    }
}
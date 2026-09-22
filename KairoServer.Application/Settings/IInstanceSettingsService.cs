using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Settings;

namespace KairoServer.Application.Settings
{
    public interface IInstanceSettingsService
    {
        Task<Result<InstanceSettingsView>> GetAsync(CancellationToken cancellationToken);

        Task<Result<InstanceSettingsView>> UpdateAsync(
            UpdateInstanceSettingsRequest request,
            string? actorId,
            CancellationToken cancellationToken);
    }
}
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Settings;

namespace NuntivusServer.Application.Settings
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
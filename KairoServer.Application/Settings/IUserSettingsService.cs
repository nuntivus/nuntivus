using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Settings;

namespace KairoServer.Application.Settings
{
    public interface IUserSettingsService
    {
        Task<Result<UserPreferencesSettingsView>> GetPreferencesAsync(
            string subject,
            CancellationToken cancellationToken);

        Task<Result<UserPreferencesSettingsView>> UpdatePreferencesAsync(
            string subject,
            UpdateUserPreferencesRequest request,
            CancellationToken cancellationToken);
    }
}
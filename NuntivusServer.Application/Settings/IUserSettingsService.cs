using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Settings;

namespace NuntivusServer.Application.Settings
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
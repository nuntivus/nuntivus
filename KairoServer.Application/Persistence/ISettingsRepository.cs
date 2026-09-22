using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Settings;
using KairoServer.Common;

namespace KairoServer.Application.Persistence
{
    public interface ISettingsRepository
    {
        Task<Result<InstanceSettingsState?>> GetInstanceSettingsAsync(CancellationToken cancellationToken);

        Task<Result> ReplaceInstanceSettingsAsync(
            InstanceSettingsState state,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result<UserPreferencesState?>> GetUserPreferencesAsync(
            string subject,
            CancellationToken cancellationToken);

        Task<Result> UpsertUserPreferencesAsync(
            string subject,
            UserPreferencesState state,
            IUnitOfWork uow,
            CancellationToken cancellationToken);
    }
}
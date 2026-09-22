using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Settings;

namespace KairoServer.Application.Settings
{
    public sealed class UserSettingsService : IUserSettingsService
    {
        private readonly ISettingsRepository _repository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<UserSettingsService> _logger;

        public UserSettingsService(
            ISettingsRepository repository,
            IUnitOfWork unitOfWork,
            IAppLogger<UserSettingsService> logger)
        {
            _repository = repository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<UserPreferencesSettingsView>> GetPreferencesAsync(
            string subject,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return Result.Failure<UserPreferencesSettingsView>(Error.Validation("subject is required."));

            var result = await _repository.GetUserPreferencesAsync(subject.Trim(), cancellationToken);
            if (result.IsFailure)
                return Result<UserPreferencesSettingsView>.Fail(result.Errors);

            var state = result.Value ?? DefaultState();
            return Result.Success(ToView(state));
        }

        public async Task<Result<UserPreferencesSettingsView>> UpdatePreferencesAsync(
            string subject,
            UpdateUserPreferencesRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return Result.Failure<UserPreferencesSettingsView>(Error.Validation("subject is required."));

            var validation = Validate(request);
            if (validation is not null)
                return Result.Failure<UserPreferencesSettingsView>(validation);

            var state = new UserPreferencesState(
                request.Theme.Trim(),
                request.DefaultPageSize,
                request.DenseTables,
                DateTimeOffset.UtcNow);

            try
            {
                _unitOfWork.Begin(true);
                var write = await _repository.UpsertUserPreferencesAsync(subject.Trim(), state, _unitOfWork, cancellationToken);
                if (write.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<UserPreferencesSettingsView>.Fail(write.Errors);
                }

                _unitOfWork.Commit();
                return Result.Success(ToView(state));
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to update user preferences for subject {Subject}.", subject);
                return Result.Failure<UserPreferencesSettingsView>(ex);
            }
        }

        private static Error? Validate(UpdateUserPreferencesRequest request)
        {
            if (request is null)
                return Error.Validation("request is required.");

            if (string.IsNullOrWhiteSpace(request.Theme))
                return Error.Validation("theme is required.");

            if (!string.Equals(request.Theme, "light", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.Theme, "dark", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.Theme, "system", StringComparison.OrdinalIgnoreCase))
            {
                return Error.Validation("theme must be one of: light, dark, system.");
            }

            if (request.DefaultPageSize is < 10 or > 200)
                return Error.Validation("defaultPageSize must be between 10 and 200.");

            return null;
        }

        private static UserPreferencesState DefaultState() =>
            new("system", 20, false, DateTimeOffset.UtcNow);

        private static UserPreferencesSettingsView ToView(UserPreferencesState state) =>
            new(state.Theme, state.DefaultPageSize, state.DenseTables, state.UpdatedAt);
    }
}
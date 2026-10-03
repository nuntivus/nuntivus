using System.Text.Json;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Domain.Auditing;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed class SourceService : ISourceService
    {
        private readonly ISourceRepository _sources;
        private readonly IAuditLogRepository _audits;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<SourceService> _logger;
        private readonly ISourceConfigurationRegistry _configurations;

        public SourceService(ISourceRepository sources, IAuditLogRepository audits, IUnitOfWork unitOfWork,
            IAppLogger<SourceService> logger, ISourceConfigurationRegistry configurations)
        {
            _sources = sources;
            _audits = audits;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _configurations = configurations;
        }

        public async Task<Result<Source>> CreateAsync(string name, string type, string configuration,
            string? description, CancellationToken cancellationToken, string? actorId = null)
        {
            if (!Enum.TryParse<SourceType>(type, true, out var sourceType) || !Enum.IsDefined(sourceType))
                return Result.Failure<Source>(Error.Validation("Unknown Source type."));
            if (!_configurations.IsOperational(sourceType))
                return Result.Failure<Source>(Error.Validation($"Source type '{sourceType}' is not operational."));
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
                return Result.Failure<Source>(Error.Validation("Source name must contain 1-200 characters."));
            if (description?.Length > 1000)
                return Result.Failure<Source>(Error.Validation("Source description is too long."));

            var config = _configurations.Normalize(sourceType, configuration);
            if (config.IsFailure) return Result<Source>.Fail(config.Errors);
            var source = Source.Create(Guid.NewGuid().ToString("D"), name, sourceType,
                config.Value, true, description);
            return await SaveAsync(source, "created", () => _sources.InsertAsync(source, _unitOfWork, cancellationToken),
                cancellationToken, actorId);
        }

        public async Task<Result<Source>> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(id, out var sourceId))
                return Result.Failure<Source>(Error.Validation("Source id must be a GUID."));
            var result = await _sources.GetByIdAsync(sourceId.ToString("D"), cancellationToken);
            if (result.IsFailure) return Result<Source>.Fail(result.Errors);
            return result.Value is null
                ? Result.Failure<Source>(Error.NotFound($"Source '{id}' was not found."))
                : Result.Success(result.Value);
        }

        public Task<Result<PagedResult<Source>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            page < 1 || pageSize is < 1 or > 100
                ? Task.FromResult(Result.Failure<PagedResult<Source>>(Error.Validation("page must be positive and pageSize must be between 1 and 100.")))
                : _sources.ListAsync(page, pageSize, cancellationToken);

        public async Task<Result<Source>> UpdateAsync(string id, string name, string configuration,
            string? description, CancellationToken cancellationToken, string? actorId = null)
        {
            var existing = await GetByIdAsync(id, cancellationToken);
            if (existing.IsFailure) return existing;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || description?.Length > 1000)
                return Result.Failure<Source>(Error.Validation("Source name or description is invalid."));
            var config = _configurations.Normalize(existing.Value.Type, configuration);
            if (config.IsFailure) return Result<Source>.Fail(config.Errors);
            var source = existing.Value;
            source.Update(name, description, config.Value, DateTimeOffset.UtcNow);
            return await SaveAsync(source, "updated", () => _sources.UpdateAsync(source, _unitOfWork, cancellationToken),
                cancellationToken, actorId);
        }

        public Task<Result<Source>> EnableAsync(string id, CancellationToken cancellationToken, string? actorId = null) =>
            ToggleAsync(id, true, cancellationToken, actorId);

        public Task<Result<Source>> DisableAsync(string id, CancellationToken cancellationToken, string? actorId = null) =>
            ToggleAsync(id, false, cancellationToken, actorId);

        private async Task<Result<Source>> ToggleAsync(string id, bool enabled, CancellationToken cancellationToken, string? actorId)
        {
            var existing = await GetByIdAsync(id, cancellationToken);
            if (existing.IsFailure || existing.Value.Enabled == enabled) return existing;
            var source = existing.Value;
            if (enabled) source.Enable(DateTimeOffset.UtcNow); else source.Disable(DateTimeOffset.UtcNow);
            return await SaveAsync(source, enabled ? "enabled" : "disabled",
                () => _sources.UpdateAsync(source, _unitOfWork, cancellationToken), cancellationToken, actorId);
        }

        public async Task<Result> DeleteAsync(string id, CancellationToken cancellationToken, string? actorId = null)
        {
            var existing = await GetByIdAsync(id, cancellationToken);
            if (existing.IsFailure) return Result.Failure(existing.Errors);
            var result = await SaveAsync(existing.Value, "deleted",
                () => _sources.DeleteAsync(existing.Value.Id.Value, _unitOfWork, cancellationToken),
                cancellationToken, actorId);
            return result.IsFailure ? Result.Failure(result.Errors) : Result.Success();
        }

        private async Task<Result<Source>> SaveAsync(Source source, string action, Func<Task<Result>> write,
            CancellationToken cancellationToken, string? actorId)
        {
            try
            {
                _unitOfWork.Begin(true);
                var result = await write();
                if (result.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<Source>.Fail(result.Errors);
                }

                var now = DateTimeOffset.UtcNow;
                var audit = AuditLogEntry.Create("source", action, actorId, null, null, null, null, null,
                    JsonSerializer.Serialize(new { sourceId = source.Id.Value, source.Name, source.Type, source.Enabled,
                        source.Description, source.Configuration }), now, now.AddDays(90));
                var recorded = await _audits.AddAsync(audit, _unitOfWork, cancellationToken);
                if (recorded.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<Source>.Fail(recorded.Errors);
                }
                _unitOfWork.Commit();
                _logger.Information("Source {Action} (SourceId {SourceId}, SourceType {SourceType}).",
                    action, source.Id.Value, source.Type);
                return Result.Success(source);
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Source operation failed (SourceId {SourceId}).", source.Id.Value);
                return Result.Failure<Source>(ex);
            }
        }
    }
}

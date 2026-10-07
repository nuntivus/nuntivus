using System.Text.Json;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Auditing;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Contracts.Destinations
{
    public sealed class DestinationService : IDestinationService
    {
        private readonly IDestinationRepository _repository;
        private readonly IDestinationConfigDispatcher _configs;
        private readonly IDestinationConfigUpdateResolver _updates;
        private readonly IAuditLogRepository _audits;
        private readonly IUnitOfWork _uow;
        private readonly IAppLogger<DestinationService> _logger;

        public DestinationService(IDestinationRepository repository, IDestinationConfigDispatcher configs,
            IDestinationConfigUpdateResolver updates,
            IAuditLogRepository audits, IUnitOfWork uow, IAppLogger<DestinationService> logger)
        {
            _repository = repository;
            _configs = configs;
            _updates = updates;
            _audits = audits;
            _uow = uow;
            _logger = logger;
        }

        public async Task<Result<DestinationResource>> CreateAsync(string name, string? description,
            DestinationConfig configuration, CancellationToken cancellationToken, string? actorId = null)
        {
            var validation = Validate(name, description, configuration);
            if (validation.IsFailure) return Result<DestinationResource>.Fail(validation.Errors);
            var now = DateTimeOffset.UtcNow;
            var resource = new DestinationResource(Guid.NewGuid(), name.Trim(), description?.Trim(),
                validation.Value, true, "continue", now, now);
            return await SaveAsync(resource, "created", () => _repository.InsertAsync(resource, _uow, cancellationToken),
                cancellationToken, actorId);
        }

        public async Task<Result<DestinationResource>> UpdateAsync(Guid id, string name, string? description,
            DestinationConfig configuration, CancellationToken cancellationToken, string? actorId = null)
        {
            var existing = await GetByIdAsync(id, cancellationToken);
            if (existing.IsFailure) return existing;
            var resolved = _updates.Resolve(configuration, existing.Value.Destination);
            if (resolved.IsFailure) return Result<DestinationResource>.Fail(resolved.Errors);
            var validation = Validate(name, description, resolved.Value);
            if (validation.IsFailure) return Result<DestinationResource>.Fail(validation.Errors);
            if (validation.Value.Type != existing.Value.Destination.Type)
                return Result.Failure<DestinationResource>(Error.Validation("Destination type cannot be changed."));
            var resource = existing.Value with { Name = name.Trim(), Description = description?.Trim(),
                Destination = validation.Value, UpdatedAt = DateTimeOffset.UtcNow };
            return await SaveAsync(resource, "updated", () => _repository.UpdateAsync(resource, _uow, cancellationToken),
                cancellationToken, actorId);
        }

        public async Task<Result<DestinationResource>> SetEnabledAsync(Guid id, bool enabled,
            bool pauseExistingBindings, CancellationToken cancellationToken, string? actorId = null)
        {
            var existing = await GetByIdAsync(id, cancellationToken);
            if (existing.IsFailure) return existing;
            var resource = existing.Value with { Enabled = enabled,
                DisablePolicy = enabled ? "continue" : pauseExistingBindings ? "pause" : "continue",
                UpdatedAt = DateTimeOffset.UtcNow };
            return await SaveAsync(resource, enabled ? "enabled" : "disabled",
                () => _repository.UpdateAsync(resource, _uow, cancellationToken), cancellationToken, actorId);
        }

        public async Task<Result> DeleteAsync(Guid id, bool removeBindings, CancellationToken cancellationToken, string? actorId = null)
        {
            var existing = await GetByIdAsync(id, cancellationToken);
            if (existing.IsFailure) return Result.Failure(existing.Errors);
            var saved = await SaveAsync(existing.Value, removeBindings ? "deleted_with_bindings" : "deleted_detached",
                () => _repository.DeleteAsync(id, removeBindings, _uow, cancellationToken), cancellationToken, actorId);
            return saved.IsFailure ? Result.Failure(saved.Errors) : Result.Success();
        }

        public async Task<Result<DestinationResource>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return Result.Failure<DestinationResource>(Error.Validation("Destination ID is required."));
            var result = await _repository.GetByIdAsync(id, cancellationToken);
            return result.IsFailure ? Result<DestinationResource>.Fail(result.Errors)
                : result.Value is null ? Result.Failure<DestinationResource>(Error.NotFound($"Destination '{id}' was not found."))
                : Result.Success(result.Value);
        }

        public Task<Result<PagedResult<DestinationResource>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            page < 1 || pageSize is < 1 or > 100
                ? Task.FromResult(Result.Failure<PagedResult<DestinationResource>>(Error.Validation("Invalid pagination.")))
                : _repository.ListAsync(page, pageSize, cancellationToken);

        private Result<Destination> Validate(string name, string? description, DestinationConfig configuration)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || description?.Length > 1000)
                return Result.Failure<Destination>(Error.Validation("Destination name or description is invalid."));
            return _configs.Build(configuration);
        }

        private async Task<Result<DestinationResource>> SaveAsync(DestinationResource resource, string action,
            Func<Task<Result>> write, CancellationToken cancellationToken, string? actorId)
        {
            try
            {
                _uow.Begin(true);
                var result = await write();
                if (result.IsFailure) { _uow.Rollback(); return Result<DestinationResource>.Fail(result.Errors); }
                var now = DateTimeOffset.UtcNow;
                var audit = AuditLogEntry.Create("destination", action, actorId, null, null, null, null, null,
                    JsonSerializer.Serialize(new { resource.Id, resource.Name, Type = resource.Destination.Type.ToString(),
                        resource.Enabled, resource.DisablePolicy }), now, now.AddDays(90));
                var logged = await _audits.AddAsync(audit, _uow, cancellationToken);
                if (logged.IsFailure) { _uow.Rollback(); return Result<DestinationResource>.Fail(logged.Errors); }
                _uow.Commit();
                return Result.Success(resource);
            }
            catch (Exception ex)
            {
                _uow.Rollback();
                _logger.Error(ex, "Destination operation failed (DestinationId {DestinationId}).", resource.Id);
                return Result.Failure<DestinationResource>(ex);
            }
        }
    }
}

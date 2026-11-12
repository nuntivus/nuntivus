using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.Events;
using NuntivusServer.Domain.Events;

using ContractSchemaCompatibilityMode = NuntivusServer.Contracts.Events.SchemaCompatibilityMode;
using ContractSchemaState = NuntivusServer.Contracts.Events.EventSchemaState;

namespace NuntivusServer.Application.Events
{
    public sealed class EventDefinitionService : IEventDefinitionService
    {
        public const int MaxPageSize = 100;

        private readonly IEventDefinitionRepository _repository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<EventDefinitionService> _logger;
        private readonly IEventSchemaValidator _schemaValidator;
        private readonly IEventSchemaEvolutionService _evolution;

        public EventDefinitionService(
            IEventDefinitionRepository repository,
            IUnitOfWork unitOfWork,
            IAppLogger<EventDefinitionService> logger,
            IEventSchemaValidator schemaValidator,
            IEventSchemaEvolutionService evolution)
        {
            _repository = repository;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _schemaValidator = schemaValidator;
            _evolution = evolution;
        }

        public async Task<Result<EventDefinitionCreated>> CreateAsync(
            string eventType,
            string displayName,
            string? description,
            CancellationToken cancellationToken)
        {
            if (!EventDefinitionValidation.IsValidEventType(eventType))
                return Result.Failure<EventDefinitionCreated>(Error.Validation(
                    "eventType is invalid. Use dotted lowercase identifiers (e.g. 'orders.created')."));

            if (!EventDefinitionValidation.IsValidDisplayName(displayName))
                return Result.Failure<EventDefinitionCreated>(Error.Validation(
                    $"displayName is required and must be at most {EventDefinitionValidation.DisplayNameMaxLength} characters."));

            if (!EventDefinitionValidation.IsValidDescription(description))
                return Result.Failure<EventDefinitionCreated>(Error.Validation(
                    $"description must be at most {EventDefinitionValidation.DescriptionMaxLength} characters."));

            try
            {
                var definition = EventDefinition.Create(eventType, displayName, description, DateTimeOffset.UtcNow);

                _unitOfWork.Begin(true);
                var insert = await _repository.InsertAsync(definition, _unitOfWork, cancellationToken);
                if (insert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<EventDefinitionCreated>.Fail(insert.Errors);
                }

                _unitOfWork.Commit();

                _logger.Information("Event definition created for type {EventType}.", definition.EventType);

                return Result.Success(definition.ToCreated());
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to create event definition for type {EventType}.", eventType);
                return Result.Failure<EventDefinitionCreated>(ex);
            }
        }

        public async Task<Result<EventDefinitionView>> GetByEventTypeAsync(
            string eventType,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                return Result.Failure<EventDefinitionView>(Error.Validation("eventType is required."));

            var result = await _repository.GetByEventTypeAsync(eventType.Trim(), cancellationToken);
            if (result.IsFailure)
                return Result<EventDefinitionView>.Fail(result.Errors);

            var definition = result.Value;
            if (definition is null)
                return Result.Failure<EventDefinitionView>(Error.NotFound($"Event definition '{eventType}' was not found."));

            return Result.Success(definition.ToView());
        }

        public async Task<Result<PagedResult<EventDefinitionView>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (page < 1)
                return Result.Failure<PagedResult<EventDefinitionView>>(
                    Error.Validation("page must be greater than or equal to 1."));

            if (pageSize < 1 || pageSize > MaxPageSize)
                return Result.Failure<PagedResult<EventDefinitionView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));

            var result = await _repository.ListAsync(page, pageSize, cancellationToken);
            if (result.IsFailure)
                return Result<PagedResult<EventDefinitionView>>.Fail(result.Errors);

            return Result.Success(result.Value.Map(d => d.ToView()));
        }

        public async Task<Result<EventDefinitionView>> UpdateAsync(
            string eventType,
            string displayName,
            string? description,
            CancellationToken cancellationToken)
        {
            if (!EventDefinitionValidation.IsValidEventType(eventType))
                return Result.Failure<EventDefinitionView>(Error.Validation("eventType is invalid."));

            if (!EventDefinitionValidation.IsValidDisplayName(displayName))
                return Result.Failure<EventDefinitionView>(Error.Validation(
                    $"displayName is required and must be at most {EventDefinitionValidation.DisplayNameMaxLength} characters."));

            if (!EventDefinitionValidation.IsValidDescription(description))
                return Result.Failure<EventDefinitionView>(Error.Validation(
                    $"description must be at most {EventDefinitionValidation.DescriptionMaxLength} characters."));

            var normalizedEventType = eventType.Trim();
            var normalizedDisplayName = displayName.Trim();
            var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

            try
            {
                _unitOfWork.Begin(true);

                var updatedRows = await _repository.UpdateAsync(
                    normalizedEventType,
                    normalizedDisplayName,
                    normalizedDescription,
                    _unitOfWork,
                    cancellationToken);

                if (updatedRows.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<EventDefinitionView>.Fail(updatedRows.Errors);
                }

                if (updatedRows.Value == 0)
                {
                    _unitOfWork.Rollback();
                    return Result.Failure<EventDefinitionView>(Error.NotFound(
                        $"Event definition '{normalizedEventType}' was not found."));
                }

                _unitOfWork.Commit();
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to update event definition for {EventType}.", normalizedEventType);
                return Result.Failure<EventDefinitionView>(ex);
            }

            return await GetByEventTypeAsync(normalizedEventType, cancellationToken);
        }

        public async Task<Result<EventSchemaVersionView>> CreateSchemaDraftAsync(
            string eventType,
            int version,
            string schema,
            ContractSchemaCompatibilityMode compatibilityMode,
            CancellationToken cancellationToken)
        {
            if (!EventDefinitionValidation.IsValidEventType(eventType))
                return Result.Failure<EventSchemaVersionView>(Error.Validation("eventType is invalid."));

            if (!EventDefinitionValidation.IsValidSchemaVersion(version))
                return Result.Failure<EventSchemaVersionView>(Error.Validation("version must be greater than 0."));

            var schemaValidation = _schemaValidator.ValidateSchema(schema);
            if (schemaValidation.IsFailure)
                return Result<EventSchemaVersionView>.Fail(schemaValidation.Errors);

            if (!EventDefinitionValidation.IsValidCompatibilityMode(EventDefinitionMapping.MapCompatibilityMode(compatibilityMode)))
                return Result.Failure<EventSchemaVersionView>(Error.Validation("compatibilityMode is invalid."));

            var definitionLookup = await _repository.GetByEventTypeAsync(eventType.Trim(), cancellationToken);
            if (definitionLookup.IsFailure)
                return Result<EventSchemaVersionView>.Fail(definitionLookup.Errors);

            if (definitionLookup.Value is null)
                return Result.Failure<EventSchemaVersionView>(Error.NotFound($"Event definition '{eventType}' was not found."));

            var existing = await _repository.GetSchemaVersionAsync(eventType.Trim(), version, cancellationToken);
            if (existing.IsFailure)
                return Result<EventSchemaVersionView>.Fail(existing.Errors);

            if (existing.Value is not null)
                return Result.Failure<EventSchemaVersionView>(Error.Conflict(
                    $"Schema version {version} already exists for '{eventType}'."));

            try
            {
                var draft = EventSchemaVersion.CreateDraft(
                    eventType,
                    version,
                    schema,
                    EventDefinitionMapping.MapCompatibilityMode(compatibilityMode),
                    DateTimeOffset.UtcNow);

                _unitOfWork.Begin(true);
                var insert = await _repository.InsertSchemaVersionAsync(draft, _unitOfWork, cancellationToken);
                if (insert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<EventSchemaVersionView>.Fail(insert.Errors);
                }

                _unitOfWork.Commit();
                return Result.Success(draft.ToView());
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex,
                    "Failed to create draft schema version {Version} for {EventType}.",
                    version,
                    eventType);
                return Result.Failure<EventSchemaVersionView>(ex);
            }
        }

        public Task<Result<EventSchemaVersionView>> ActivateSchemaVersionAsync(
            string eventType, int version, CancellationToken cancellationToken,
            string? actorId = null, bool canOverride = false, SchemaActivationOptions? options = null) =>
            _evolution.ActivateAsync(eventType, version, cancellationToken, actorId, canOverride, options);

        public Task<Result<SchemaCompatibilityReport>> CompareSchemaVersionsAsync(
            string eventType, int version, int? againstVersion, CancellationToken cancellationToken) =>
            _evolution.CompareAsync(eventType, version, againstVersion, cancellationToken);

        public async Task<Result<EventSchemaVersionView>> DeprecateSchemaVersionAsync(
            string eventType,
            int version,
            CancellationToken cancellationToken)
        {
            var schemaResult = await GetSchemaVersionAsync(eventType, version, cancellationToken);
            if (schemaResult.IsFailure)
                return schemaResult;

            try
            {
                var now = DateTimeOffset.UtcNow;

                _unitOfWork.Begin(true);
                var deprecate = await _repository.DeprecateSchemaVersionAsync(
                    eventType.Trim(),
                    version,
                    now,
                    _unitOfWork,
                    cancellationToken);

                if (deprecate.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<EventSchemaVersionView>.Fail(deprecate.Errors);
                }

                _unitOfWork.Commit();
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex,
                    "Failed to deprecate schema version {Version} for {EventType}.",
                    version,
                    eventType);
                return Result.Failure<EventSchemaVersionView>(ex);
            }

            return await GetSchemaVersionAsync(eventType, version, cancellationToken);
        }

        public async Task<Result<EventSchemaVersionView>> GetSchemaVersionAsync(
            string eventType,
            int version,
            CancellationToken cancellationToken)
        {
            if (!EventDefinitionValidation.IsValidEventType(eventType))
                return Result.Failure<EventSchemaVersionView>(Error.Validation("eventType is invalid."));

            if (!EventDefinitionValidation.IsValidSchemaVersion(version))
                return Result.Failure<EventSchemaVersionView>(Error.Validation("version must be greater than 0."));

            var result = await _repository.GetSchemaVersionAsync(eventType.Trim(), version, cancellationToken);
            if (result.IsFailure)
                return Result<EventSchemaVersionView>.Fail(result.Errors);

            if (result.Value is null)
                return Result.Failure<EventSchemaVersionView>(Error.NotFound(
                    $"Schema version {version} for '{eventType}' was not found."));

            return Result.Success(result.Value.ToView());
        }

        public async Task<Result<PagedResult<EventSchemaVersionView>>> ListSchemaVersionsAsync(
            string eventType,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (!EventDefinitionValidation.IsValidEventType(eventType))
                return Result.Failure<PagedResult<EventSchemaVersionView>>(Error.Validation("eventType is invalid."));

            if (page < 1)
                return Result.Failure<PagedResult<EventSchemaVersionView>>(
                    Error.Validation("page must be greater than or equal to 1."));

            if (pageSize < 1 || pageSize > MaxPageSize)
                return Result.Failure<PagedResult<EventSchemaVersionView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));

            var result = await _repository.ListSchemaVersionsAsync(eventType.Trim(), page, pageSize, cancellationToken);
            if (result.IsFailure)
                return Result<PagedResult<EventSchemaVersionView>>.Fail(result.Errors);

            return Result.Success(result.Value.Map(v => v.ToView()));
        }
    }
}
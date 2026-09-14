using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Events;
using KairoServer.Domain.Events;

namespace KairoServer.Application.Events
{
    public sealed class EventDefinitionService : IEventDefinitionService
    {
        public const int MaxPageSize = 100;

        private readonly IEventDefinitionRepository _repository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<EventDefinitionService> _logger;

        public EventDefinitionService(
            IEventDefinitionRepository repository,
            IUnitOfWork unitOfWork,
            IAppLogger<EventDefinitionService> logger)
        {
            _repository = repository;
            _unitOfWork = unitOfWork;
            _logger = logger;
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
    }
}
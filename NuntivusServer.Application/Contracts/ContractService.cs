using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Contracts.Destinations;
using NuntivusServer.Domain.Auditing;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Contracts
{
    public sealed class ContractService : IContractService
    {
        public const int MaxPageSize = 100;
        public const int DefaultPageSize = 20;

        private readonly IContractRepository _repository;
        private readonly ICustomerRepository _customers;
        private readonly ISourceRepository _sources;
        private readonly IDestinationRepository _destinationResources;
        private readonly IEventDefinitionRepository _eventDefinitions;
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDestinationConfigDispatcher _destinations;
        private readonly IDestinationConfigUpdateResolver _destinationUpdates;
        private readonly IAppLogger<ContractService> _logger;

        public ContractService(
            IContractRepository repository,
            ICustomerRepository customers,
            ISourceRepository sources,
            IDestinationRepository destinationResources,
            IEventDefinitionRepository eventDefinitions,
            IAuditLogRepository auditLogs,
            IUnitOfWork unitOfWork,
            IDestinationConfigDispatcher destinations,
            IDestinationConfigUpdateResolver destinationUpdates,
            IAppLogger<ContractService> logger)
        {
            _repository = repository;
            _customers = customers;
            _sources = sources;
            _destinationResources = destinationResources;
            _eventDefinitions = eventDefinitions;
            _auditLogs = auditLogs;
            _unitOfWork = unitOfWork;
            _destinations = destinations;
            _destinationUpdates = destinationUpdates;
            _logger = logger;
        }

        public async Task<Result<ContractCreated>> CreateAsync(
            CreateContractRequest request,
            CancellationToken cancellationToken,
            string? actorId = null)
        {
            if (request is null)
                return Result.Failure<ContractCreated>(Error.Validation("Request body is required."));

            if (string.IsNullOrWhiteSpace(request.CustomerId))
                return Result.Failure<ContractCreated>(Error.Validation("customerId is required."));

            if (request.EventTypes is null || request.EventTypes.Count == 0)
                return Result.Failure<ContractCreated>(Error.Validation("At least one event type is required."));

            if (!ContractValidation.IsValidName(request.Name))
                return Result.Failure<ContractCreated>(Error.Validation(
                    $"name must be null/empty or at most {ContractValidation.NameMaxLength} characters."));

            if (request.EventTypes.Count > ContractValidation.MaxEventTypesPerContract)
                return Result.Failure<ContractCreated>(Error.Validation(
                    $"A contract can reference at most {ContractValidation.MaxEventTypesPerContract} event types."));

            foreach (var et in request.EventTypes)
            {
                if (!ContractValidation.IsValidEventType(et))
                    return Result.Failure<ContractCreated>(Error.Validation(
                        $"Invalid event type '{et}'. Use dotted lowercase identifiers (e.g. 'orders.created')."));
            }

            var schemaTargets = request.SchemaTargets ?? Array.Empty<ContractSchemaTargetRequest>();
            if (schemaTargets.Count > 0)
            {
                var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var target in schemaTargets)
                {
                    if (!ContractValidation.IsValidEventType(target.EventType))
                        return Result.Failure<ContractCreated>(Error.Validation(
                            $"Invalid schema target event type '{target.EventType}'."));

                    if (!ContractValidation.IsValidSchemaVersion(target.Version))
                        return Result.Failure<ContractCreated>(Error.Validation(
                            $"Invalid schema target version for '{target.EventType}'."));

                    if (!request.EventTypes.Contains(target.EventType, StringComparer.Ordinal))
                        return Result.Failure<ContractCreated>(Error.Validation(
                            $"Schema target event type '{target.EventType}' must exist in eventTypes."));

                    if (!seen.Add(target.EventType))
                        return Result.Failure<ContractCreated>(Error.Validation(
                            $"Duplicate schema target for event type '{target.EventType}'."));

                    var schemaLookup = await _eventDefinitions.GetSchemaVersionAsync(
                        target.EventType,
                        target.Version,
                        cancellationToken);

                    if (schemaLookup.IsFailure)
                        return Result<ContractCreated>.Fail(schemaLookup.Errors);

                    if (schemaLookup.Value is null)
                        return Result.Failure<ContractCreated>(Error.NotFound(
                            $"Schema version {target.Version} for '{target.EventType}' was not found."));
                }
            }

            var filterValidation = ValidateFilter(request.Filter);
            if (filterValidation.IsFailure)
                return Result<ContractCreated>.Fail(filterValidation.Errors);

            var transformationValidation = ValidateTransformation(request.Transformation);
            if (transformationValidation.IsFailure)
                return Result<ContractCreated>.Fail(transformationValidation.Errors);

            var rateLimitValidation = ValidateRateLimit(request.RateLimit);
            if (rateLimitValidation.IsFailure)
                return Result<ContractCreated>.Fail(rateLimitValidation.Errors);

            var orderingValidation = ValidateOrdering(request.Ordering);
            if (orderingValidation.IsFailure)
                return Result<ContractCreated>.Fail(orderingValidation.Errors);

            var destinationResult = request.DestinationIds is { Count: > 0 }
                ? null : _destinations.Build(request.Configuration);
            if (destinationResult?.IsFailure == true)
                return Result<ContractCreated>.Fail(destinationResult.Errors);

            var sourcesValidation = await ValidateSourcesAsync(request.SourceIds, [], cancellationToken);
            if (sourcesValidation.IsFailure)
                return Result<ContractCreated>.Fail(sourcesValidation.Errors);

            var customerLookup = await _customers.GetByIdAsync(request.CustomerId, cancellationToken);
            if (customerLookup.IsFailure)
                return Result<ContractCreated>.Fail(customerLookup.Errors);
            if (customerLookup.Value is null)
                return Result.Failure<ContractCreated>(Error.NotFound(
                    $"Customer '{request.CustomerId}' was not found."));

            try
            {
                var now = GetPersistedUtcNow();
                var bindingsResult = await ResolveBindingsAsync(request.DestinationIds, null, null, now, cancellationToken);
                if (bindingsResult.IsFailure)
                    return Result<ContractCreated>.Fail(bindingsResult.Errors);
                var domainSchemaTargets = new System.Collections.Generic.List<ContractSchemaTarget>(schemaTargets.Count);
                foreach (var target in schemaTargets)
                    domainSchemaTargets.Add(new ContractSchemaTarget(target.EventType, target.Version));

                var contract = Contract.Create(
                    request.CustomerId,
                    request.EventTypes,
                    domainSchemaTargets,
                    request.Filter.ToDomain(),
                    bindingsResult.Value.Count > 0 ? bindingsResult.Value[0].Destination : destinationResult!.Value,
                    now,
                    request.Transformation.ToDomain(),
                    request.RateLimit.ToDomain(),
                    request.Ordering.ToDomain(),
                    request.Name,
                    request.SourceIds);
                if (bindingsResult.Value.Count > 0)
                    contract.SetDestinations(bindingsResult.Value);

                _unitOfWork.Begin(true);
                var insert = await _repository.InsertAsync(contract, _unitOfWork, cancellationToken);
                if (insert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractCreated>.Fail(insert.Errors);
                }

                var audit = BuildAudit(
                    action: "created",
                    actorId: actorId,
                    contract: contract,
                    operationId: null,
                    details: JsonSerializer.Serialize(new
                    {
                        contract.Id,
                        contract.CustomerId,
                        contract.Enabled,
                        EventTypes = contract.EventTypes,
                        SourceIds = contract.SourceIds,
                        DestinationType = contract.Destination.Type.ToString(),
                    }),
                    createdAt: now);
                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractCreated>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();

                _logger.Information(
                    "Contract created {ContractId} for customer {CustomerId} with {EventTypeCount} event types.",
                    contract.Id, contract.CustomerId, contract.EventTypes.Count);

                return Result.Success(contract.ToCreated());
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to create contract for customer {CustomerId}.", request.CustomerId);
                return Result.Failure<ContractCreated>(ex);
            }
        }

        public async Task<Result<ContractView>> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
                return Result.Failure<ContractView>(Error.Validation("Contract id is required."));

            var result = await _repository.GetByIdAsync(id, cancellationToken);
            if (result.IsFailure)
                return Result<ContractView>.Fail(result.Errors);

            var contract = result.Value;
            if (contract is null)
                return Result.Failure<ContractView>(Error.NotFound($"Contract '{id}' was not found."));

            return MapToView(contract);
        }

        public async Task<Result<PagedResult<ContractView>>> ListByCustomerAsync(
            ContractListQuery query,
            CancellationToken cancellationToken)
        {
            if (query is null)
                return Result.Failure<PagedResult<ContractView>>(Error.Validation("query is required."));

            if (string.IsNullOrWhiteSpace(query.CustomerId))
                return Result.Failure<PagedResult<ContractView>>(Error.Validation("customerId is required."));

            if (query.Page < 1)
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation("page must be greater than or equal to 1."));

            if (query.PageSize < 1 || query.PageSize > MaxPageSize)
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));

            if (query.CreatedFrom.HasValue && query.CreatedTo.HasValue && query.CreatedFrom > query.CreatedTo)
            {
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation("createdFrom must be less than or equal to createdTo."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortBy)
                && !string.Equals(query.SortBy, "createdAt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "updatedAt", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation("sortBy must be one of: createdAt, updatedAt."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortDirection)
                && !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation("sortDirection must be one of: asc, desc."));
            }

            var result = await _repository.ListByCustomerAsync(query, cancellationToken);
            if (result.IsFailure)
                return Result<PagedResult<ContractView>>.Fail(result.Errors);

            var views = new System.Collections.Generic.List<ContractView>(result.Value.Items.Count);
            foreach (var contract in result.Value.Items)
            {
                var mapped = MapToView(contract);
                if (mapped.IsFailure)
                    return Result<PagedResult<ContractView>>.Fail(mapped.Errors);

                views.Add(mapped.Value);
            }

            return Result.Success(PagedResult<ContractView>.Create(
                views,
                result.Value.Page,
                result.Value.PageSize,
                result.Value.TotalCount));
        }

        public async Task<Result<PagedResult<ContractView>>> ListAsync(
            AdminContractListQuery query,
            CancellationToken cancellationToken)
        {
            if (query is null)
                return Result.Failure<PagedResult<ContractView>>(Error.Validation("query is required."));

            if (query.Page < 1)
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation("page must be greater than or equal to 1."));

            if (query.PageSize < 1 || query.PageSize > MaxPageSize)
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));

            if (query.CreatedFrom.HasValue && query.CreatedTo.HasValue && query.CreatedFrom > query.CreatedTo)
            {
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation("createdFrom must be less than or equal to createdTo."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortBy)
                && !string.Equals(query.SortBy, "createdAt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "updatedAt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "name", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "customerId", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "destinationType", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation("sortBy must be one of: createdAt, updatedAt, name, customerId, destinationType."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortDirection)
                && !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation("sortDirection must be one of: asc, desc."));
            }

            string? normalizedDestinationType = null;
            if (!string.IsNullOrWhiteSpace(query.DestinationType))
            {
                if (!Enum.TryParse<NuntivusServer.Domain.Contracts.DestinationType>(query.DestinationType.Trim(), ignoreCase: true, out var destinationType))
                {
                    return Result.Failure<PagedResult<ContractView>>(
                        Error.Validation("destinationType is invalid."));
                }

                normalizedDestinationType = destinationType.ToString();
            }

            string? normalizedEventType = null;
            if (!string.IsNullOrWhiteSpace(query.EventType))
            {
                if (!ContractValidation.IsValidEventType(query.EventType))
                {
                    return Result.Failure<PagedResult<ContractView>>(
                        Error.Validation("eventType is invalid. Use dotted lowercase identifiers (e.g. 'orders.created')."));
                }

                normalizedEventType = query.EventType.Trim();
            }

            var result = await _repository.ListAsync(
                query with { DestinationType = normalizedDestinationType, EventType = normalizedEventType },
                cancellationToken);

            if (result.IsFailure)
                return Result<PagedResult<ContractView>>.Fail(result.Errors);

            var views = new List<ContractView>(result.Value.Items.Count);
            foreach (var contract in result.Value.Items)
            {
                var mapped = MapToView(contract);
                if (mapped.IsFailure)
                    return Result<PagedResult<ContractView>>.Fail(mapped.Errors);

                views.Add(mapped.Value);
            }

            return Result.Success(PagedResult<ContractView>.Create(
                views,
                result.Value.Page,
                result.Value.PageSize,
                result.Value.TotalCount));
        }

        public async Task<Result<ContractView>> ReplaceAsync(
            Guid id,
            UpdateContractRequest request,
            DateTimeOffset expectedUpdatedAt,
            CancellationToken cancellationToken,
            string? actorId = null)
        {
            if (id == Guid.Empty)
                return Result.Failure<ContractView>(Error.Validation("Contract id is required."));

            if (request is null)
                return Result.Failure<ContractView>(Error.Validation("Request body is required."));

            if (!ContractValidation.IsValidName(request.Name))
                return Result.Failure<ContractView>(Error.Validation(
                    $"name must be null/empty or at most {ContractValidation.NameMaxLength} characters."));

            if (request.EventTypes is null || request.EventTypes.Count == 0)
                return Result.Failure<ContractView>(Error.Validation("At least one event type is required."));

            if (request.EventTypes.Count > ContractValidation.MaxEventTypesPerContract)
                return Result.Failure<ContractView>(Error.Validation(
                    $"A contract can reference at most {ContractValidation.MaxEventTypesPerContract} event types."));

            foreach (var et in request.EventTypes)
            {
                if (!ContractValidation.IsValidEventType(et))
                    return Result.Failure<ContractView>(Error.Validation(
                        $"Invalid event type '{et}'. Use dotted lowercase identifiers (e.g. 'orders.created')."));
            }

            var schemaTargets = request.SchemaTargets ?? Array.Empty<ContractSchemaTargetRequest>();
            if (schemaTargets.Count > 0)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var target in schemaTargets)
                {
                    if (!ContractValidation.IsValidEventType(target.EventType))
                        return Result.Failure<ContractView>(Error.Validation(
                            $"Invalid schema target event type '{target.EventType}'."));

                    if (!ContractValidation.IsValidSchemaVersion(target.Version))
                        return Result.Failure<ContractView>(Error.Validation(
                            $"Invalid schema target version for '{target.EventType}'."));

                    if (!request.EventTypes.Contains(target.EventType, StringComparer.Ordinal))
                        return Result.Failure<ContractView>(Error.Validation(
                            $"Schema target event type '{target.EventType}' must exist in eventTypes."));

                    if (!seen.Add(target.EventType))
                        return Result.Failure<ContractView>(Error.Validation(
                            $"Duplicate schema target for event type '{target.EventType}'."));

                    var schemaLookup = await _eventDefinitions.GetSchemaVersionAsync(
                        target.EventType,
                        target.Version,
                        cancellationToken);

                    if (schemaLookup.IsFailure)
                        return Result<ContractView>.Fail(schemaLookup.Errors);

                    if (schemaLookup.Value is null)
                        return Result.Failure<ContractView>(Error.NotFound(
                            $"Schema version {target.Version} for '{target.EventType}' was not found."));
                }
            }

            var filterValidation = ValidateFilter(request.Filter);
            if (filterValidation.IsFailure)
                return Result<ContractView>.Fail(filterValidation.Errors);

            var transformationValidation = ValidateTransformation(request.Transformation);
            if (transformationValidation.IsFailure)
                return Result<ContractView>.Fail(transformationValidation.Errors);

            var rateLimitValidation = ValidateRateLimit(request.RateLimit);
            if (rateLimitValidation.IsFailure)
                return Result<ContractView>.Fail(rateLimitValidation.Errors);

            var orderingValidation = ValidateOrdering(request.Ordering);
            if (orderingValidation.IsFailure)
                return Result<ContractView>.Fail(orderingValidation.Errors);

            try
            {
                var lookup = await _repository.GetByIdAsync(id, cancellationToken);
                if (lookup.IsFailure)
                    return Result<ContractView>.Fail(lookup.Errors);

                var existing = lookup.Value;
                if (existing is null)
                    return Result.Failure<ContractView>(Error.NotFound($"Contract '{id}' was not found."));

                if (existing.UpdatedAt != expectedUpdatedAt)
                    return Result.Failure<ContractView>(Error.Conflict("If-Match ETag does not match current contract version."));

                var sourcesValidation = await ValidateSourcesAsync(request.SourceIds, existing.SourceIds, cancellationToken);
                if (sourcesValidation.IsFailure)
                    return Result<ContractView>.Fail(sourcesValidation.Errors);

                Result<Destination>? destinationResult = null;
                if (request.DestinationIds is not { Count: > 0 } && request.Configuration is not null)
                {
                    var resolved = _destinationUpdates.Resolve(request.Configuration, existing.Destination);
                    if (resolved.IsFailure)
                        return Result<ContractView>.Fail(resolved.Errors);
                    destinationResult = _destinations.Build(resolved.Value);
                    if (destinationResult.IsFailure)
                        return Result<ContractView>.Fail(destinationResult.Errors);
                }

                var now = GetPersistedUtcNow();
                var bindingsResult = await ResolveBindingsAsync(request.DestinationIds, existing.Destinations,
                    request.RefreshDestinationIds, now, cancellationToken, request.RemoveDestinationBindingIds);
                if (bindingsResult.IsFailure)
                    return Result<ContractView>.Fail(bindingsResult.Errors);
                if (bindingsResult.Value.Count == 0)
                    return Result.Failure<ContractView>(Error.Validation("Select at least one Destination."));
                var domainSchemaTargets = new List<ContractSchemaTarget>(schemaTargets.Count);
                foreach (var target in schemaTargets)
                    domainSchemaTargets.Add(new ContractSchemaTarget(target.EventType, target.Version));

                var updated = Contract.Rehydrate(
                    existing.Id,
                    existing.CustomerId,
                    request.Name,
                    existing.Enabled,
                    request.EventTypes,
                    domainSchemaTargets,
                    request.Filter.ToDomain(),
                    bindingsResult.Value.Count > 0 ? bindingsResult.Value[0].Destination : destinationResult?.Value ?? existing.Destination,
                    existing.CreatedAt,
                    now,
                    request.Transformation.ToDomain(),
                    request.RateLimit.ToDomain(),
                    request.Ordering.ToDomain(),
                    request.SourceIds);
                var updatedBindings = bindingsResult.Value;
                if (request.DestinationIds is null && destinationResult is not null && updatedBindings.Count > 0)
                    updatedBindings = updatedBindings.Select((binding, index) => index == 0
                        ? binding with { Destination = destinationResult.Value } : binding).ToArray();
                updated.SetDestinations(updatedBindings);

                _unitOfWork.Begin(true);
                var update = await _repository.UpdateAsync(updated, _unitOfWork, cancellationToken);
                if (update.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractView>.Fail(update.Errors);
                }

                var audit = BuildAudit(
                    action: "updated",
                    actorId: actorId,
                    contract: updated,
                    operationId: null,
                    details: JsonSerializer.Serialize(new
                    {
                        updated.Id,
                        updated.CustomerId,
                        updated.Name,
                        EventTypes = updated.EventTypes,
                        SourceIds = updated.SourceIds,
                        DestinationType = updated.Destination.Type.ToString(),
                    }),
                    createdAt: now);
                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractView>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();
                _logger.Information("Contract updated {ContractId}.", updated.Id);
                return MapToView(updated);
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to update contract {ContractId}.", id);
                return Result.Failure<ContractView>(ex);
            }
        }

        public Task<Result<ContractView>> EnableAsync(Guid id, CancellationToken cancellationToken, string? actorId = null) =>
            ToggleAsync(id, enable: true, cancellationToken, actorId);

        public Task<Result<ContractView>> DisableAsync(Guid id, CancellationToken cancellationToken, string? actorId = null) =>
            ToggleAsync(id, enable: false, cancellationToken, actorId);

        public Task<Result<ContractView>> UpdateCredentialsAsync(
            Guid id,
            UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken,
            string? actorId = null) =>
            UpsertCredentialsAsync(id, request, "updated", rotate: false, cancellationToken, actorId);

        public Task<Result<ContractView>> RotateCredentialsAsync(
            Guid id,
            UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken,
            string? actorId = null) =>
            UpsertCredentialsAsync(id, request, "rotated", rotate: true, cancellationToken, actorId);

        private async Task<Result<ContractView>> ToggleAsync(
            Guid id,
            bool enable,
            CancellationToken cancellationToken,
            string? actorId)
        {
            if (id == Guid.Empty)
                return Result.Failure<ContractView>(Error.Validation("Contract id is required."));

            try
            {
                var lookup = await _repository.GetByIdAsync(id, cancellationToken);
                if (lookup.IsFailure)
                    return Result<ContractView>.Fail(lookup.Errors);

                var contract = lookup.Value;
                if (contract is null)
                    return Result.Failure<ContractView>(Error.NotFound($"Contract '{id}' was not found."));

                var now = GetPersistedUtcNow();
                if (enable) contract.Enable(now); else contract.Disable(now);

                _unitOfWork.Begin(true);
                var update = await _repository.UpdateAsync(contract, _unitOfWork, cancellationToken);
                if (update.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractView>.Fail(update.Errors);
                }

                var action = enable ? "enabled" : "disabled";
                var audit = BuildAudit(
                    action,
                    actorId,
                    contract,
                    null,
                    JsonSerializer.Serialize(new
                    {
                        contract.Id,
                        contract.CustomerId,
                        contract.Enabled,
                    }),
                    now);
                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractView>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();

                _logger.Information(
                    "Contract {Action} {ContractId}.",
                    enable ? "enabled" : "disabled",
                    contract.Id);

                return MapToView(contract);
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to toggle contract {ContractId} (enable={Enable}).", id, enable);
                return Result.Failure<ContractView>(ex);
            }
        }

        private async Task<Result<ContractView>> UpsertCredentialsAsync(
            Guid id,
            UpdateContractCredentialsRequest request,
            string action,
            bool rotate,
            CancellationToken cancellationToken,
            string? actorId)
        {
            if (id == Guid.Empty)
                return Result.Failure<ContractView>(Error.Validation("Contract id is required."));

            if (request?.Configuration is null)
                return Result.Failure<ContractView>(Error.Validation("Destination configuration is required."));

            try
            {
                var lookup = await _repository.GetByIdAsync(id, cancellationToken);
                if (lookup.IsFailure)
                    return Result<ContractView>.Fail(lookup.Errors);

                var contract = lookup.Value;
                if (contract is null)
                    return Result.Failure<ContractView>(Error.NotFound($"Contract '{id}' was not found."));

                var destinationResult = _destinations.Build(request.Configuration);
                if (destinationResult.IsFailure)
                    return Result<ContractView>.Fail(destinationResult.Errors);

                var destinationToApply = destinationResult.Value;
                if (rotate)
                {
                    var rotation = _destinations.Rotate(contract.Destination, destinationResult.Value);
                    if (rotation.IsFailure)
                        return Result<ContractView>.Fail(rotation.Errors);

                    destinationToApply = rotation.Value;
                }

                var now = GetPersistedUtcNow();
                contract.UpdateDestination(destinationToApply, now);

                _unitOfWork.Begin(true);
                var update = await _repository.UpdateAsync(contract, _unitOfWork, cancellationToken);
                if (update.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractView>.Fail(update.Errors);
                }

                var audit = BuildAudit(
                    rotate ? "credentials_rotated" : "credentials_updated",
                    actorId,
                    contract,
                    null,
                    JsonSerializer.Serialize(new
                    {
                        contract.Id,
                        contract.CustomerId,
                        DestinationType = contract.Destination.Type.ToString(),
                        Rotate = rotate,
                    }),
                    now);
                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractView>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();
                _logger.Information("Contract credentials {Action} {ContractId}.", action, contract.Id);

                return MapToView(contract);
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to {Action} contract credentials {ContractId}.", action, id);
                return Result.Failure<ContractView>(ex);
            }
        }

        private async Task<Result<IReadOnlyList<ContractDestinationBinding>>> ResolveBindingsAsync(
            IReadOnlyCollection<Guid>? ids,
            IReadOnlyCollection<ContractDestinationBinding>? existing,
            IReadOnlyCollection<Guid>? refreshIds,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            IReadOnlyCollection<Guid>? removeBindingIds = null)
        {
            if (removeBindingIds is not null && removeBindingIds.Any(id =>
                existing?.Any(binding => binding.Id == id && binding.DestinationId is null) != true))
                return Result.Failure<IReadOnlyList<ContractDestinationBinding>>(
                    Error.Validation("Only detached Destination bindings can be removed."));
            var detached = existing?.Where(binding => binding.DestinationId is null &&
                removeBindingIds?.Contains(binding.Id) != true).ToArray() ?? [];
            if (ids is null)
                return Result.Success<IReadOnlyList<ContractDestinationBinding>>(
                    existing?.Where(binding => removeBindingIds?.Contains(binding.Id) != true).ToArray() ?? []);
            if ((ids.Count == 0 && detached.Length == 0) || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)
                return Result.Failure<IReadOnlyList<ContractDestinationBinding>>(
                    Error.Validation("Select at least one unique Destination ID."));
            if (refreshIds is not null && refreshIds.Except(ids).Any())
                return Result.Failure<IReadOnlyList<ContractDestinationBinding>>(
                    Error.Validation("Only selected destinations can be refreshed."));

            var bindings = new List<ContractDestinationBinding>();
            foreach (var id in ids)
            {
                var saved = existing?.FirstOrDefault(item => item.DestinationId == id);
                if (saved is not null && refreshIds?.Contains(id) != true)
                {
                    bindings.Add(saved);
                    continue;
                }

                var result = await _destinationResources.GetByIdAsync(id, cancellationToken);
                if (result.IsFailure) return Result<IReadOnlyList<ContractDestinationBinding>>.Fail(result.Errors);
                if (result.Value is null)
                    return Result.Failure<IReadOnlyList<ContractDestinationBinding>>(
                        Error.NotFound($"Destination '{id}' was not found."));
                if (!result.Value.Enabled || !result.Value.Configured)
                    return Result.Failure<IReadOnlyList<ContractDestinationBinding>>(
                        Error.Validation($"Destination '{id}' is disabled or not configured."));
                bindings.Add(new ContractDestinationBinding(saved?.Id ?? Guid.NewGuid(), id,
                    result.Value.Destination, true, saved?.CreatedAt ?? now));
            }

            bindings.AddRange(detached);
            return Result.Success<IReadOnlyList<ContractDestinationBinding>>(bindings);
        }

        private static DateTimeOffset GetPersistedUtcNow()
        {
            var now = DateTimeOffset.UtcNow;
            // PostgreSQL timestamps have microsecond precision; ETags must use the persisted value.
            return now.AddTicks(-(now.Ticks % 10));
        }

        private static AuditLogEntry BuildAudit(
            string action,
            string? actorId,
            Contract contract,
            Guid? operationId,
            string details,
            DateTimeOffset createdAt)
        {
            return AuditLogEntry.Create(
                category: "contract",
                action: action,
                actorId: actorId,
                customerId: contract.CustomerId,
                contractId: contract.Id,
                deliveryId: null,
                eventId: null,
                operationId: operationId,
                details: details,
                createdAt: createdAt,
                expiresAt: createdAt.AddDays(90));
        }

        private Result<ContractView> MapToView(Contract contract)
        {
            var redactedConfig = _destinations.Redact(contract.Destination);
            if (redactedConfig.IsFailure)
                return Result<ContractView>.Fail(redactedConfig.Errors);

            var bindings = new List<ContractDestinationView>();
            foreach (var binding in contract.Destinations)
            {
                var redacted = _destinations.Redact(binding.Destination);
                if (redacted.IsFailure) return Result<ContractView>.Fail(redacted.Errors);
                bindings.Add(new ContractDestinationView(binding.Id, binding.DestinationId,
                    ContractMapping.MapDestinationType(binding.Destination.Type), redacted.Value,
                    binding.DeliveryEnabled));
            }

            return Result.Success(new ContractView(
                contract.Id,
                contract.CustomerId,
                contract.Enabled,
                new System.Collections.Generic.List<string>(contract.EventTypes),
                ContractMapping.ToSchemaTargetViews(contract.SchemaTargets),
                ContractMapping.ToFilterView(contract.Filter),
                ContractMapping.ToTransformationView(contract.Transformation),
                ContractMapping.ToRateLimitConfig(contract.RateLimit),
                ContractMapping.ToOrderingConfig(contract.Ordering),
                ContractMapping.MapDestinationType(contract.Destination.Type),
                redactedConfig.Value,
                contract.CreatedAt,
                contract.UpdatedAt,
                contract.Name,
                new List<Guid>(contract.SourceIds),
                bindings));
        }

        private async Task<Result> ValidateSourcesAsync(
            IReadOnlyCollection<Guid>? sourceIds,
            IReadOnlyCollection<Guid> existingIds,
            CancellationToken cancellationToken)
        {
            if (sourceIds is null || sourceIds.Count == 0 || sourceIds.Count > 100
                || sourceIds.Any(id => id == Guid.Empty) || sourceIds.Distinct().Count() != sourceIds.Count)
                return Result.Failure(Error.Validation("Select 1-100 unique Source IDs."));

            foreach (var id in sourceIds)
            {
                var lookup = await _sources.GetByIdAsync(id.ToString("D"), cancellationToken);
                if (lookup.IsFailure) return Result.Failure(lookup.Errors);
                if (lookup.Value is null)
                    return Result.Failure(Error.NotFound($"Source '{id}' was not found."));
                if (!existingIds.Contains(id) && (!lookup.Value.Enabled ||
                    lookup.Value.Type is not (SourceType.HTTP_API or SourceType.RABBITMQ or SourceType.KAFKA or
                        SourceType.SQS or SourceType.NATS or SourceType.HTTP_POLLING or SourceType.FOLDER or SourceType.S3)))
                    return Result.Failure(Error.Validation($"Source '{id}' is not an enabled, operational Source."));
            }
            return Result.Success();
        }

        internal static Result ValidateOrdering(ContractOrderingConfig? ordering)
        {
            if (ordering is null)
                return Result.Success();

            if (ordering.KeySelector is null)
                return Result.Success();

            if (!ContractValidation.IsValidFilterSelector(ordering.KeySelector))
                return Result.Failure(Error.Validation(
                    "ordering.keySelector is invalid. Use dot-separated keys only."));

            return Result.Success();
        }

        internal static Result ValidateRateLimit(ContractRateLimitConfig? rateLimit)
        {
            if (rateLimit is null)
                return Result.Success();

            if (rateLimit.PerSecond is null && rateLimit.PerMinute is null)
                return Result.Failure(Error.Validation("rateLimit must set perSecond or perMinute."));

            if (rateLimit.PerSecond is <= 0 or > ContractValidation.MaxRateLimitPerSecond)
                return Result.Failure(Error.Validation(
                    $"rateLimit.perSecond must be between 1 and {ContractValidation.MaxRateLimitPerSecond}."));

            if (rateLimit.PerMinute is <= 0 or > ContractValidation.MaxRateLimitPerMinute)
                return Result.Failure(Error.Validation(
                    $"rateLimit.perMinute must be between 1 and {ContractValidation.MaxRateLimitPerMinute}."));

            if (rateLimit.Burst is <= 0 or > ContractValidation.MaxRateLimitBurst)
                return Result.Failure(Error.Validation(
                    $"rateLimit.burst must be between 1 and {ContractValidation.MaxRateLimitBurst} when provided."));

            return Result.Success();
        }

        internal static Result ValidateFilter(ContractFilterRequest? filter)
        {
            if (filter is null)
                return Result.Success();

            var metadataRules = filter.MetadataRules ?? Array.Empty<ContractFilterRuleRequest>();
            var payloadRules = filter.PayloadRules ?? Array.Empty<ContractFilterRuleRequest>();

            if (metadataRules.Count > ContractValidation.MaxFilterRulesPerScope)
                return Result.Failure(Error.Validation(
                    $"At most {ContractValidation.MaxFilterRulesPerScope} metadata filter rules are allowed."));

            if (payloadRules.Count > ContractValidation.MaxFilterRulesPerScope)
                return Result.Failure(Error.Validation(
                    $"At most {ContractValidation.MaxFilterRulesPerScope} payload filter rules are allowed."));

            var metadataValidation = ValidateFilterRules(metadataRules, "metadata");
            if (metadataValidation.IsFailure)
                return metadataValidation;

            return ValidateFilterRules(payloadRules, "payload");
        }

        internal static Result ValidateTransformation(ContractPayloadTransformationRequest? transformation)
        {
            if (transformation is null)
                return Result.Success();

            if (transformation.Version == 2)
            {
                if (transformation.FieldMappings is { Count: > 0 } || transformation.Expression is null)
                    return Result.Failure(Error.Validation("Version 2 requires an expression and no legacy field mappings."));
                return NuntivusServer.Application.Deliveries.Execution.TypedPayloadTransformation.Validate(transformation.Expression.Value);
            }
            if (transformation.Version != 1 || transformation.Expression is not null)
                return Result.Failure(Error.Validation("Use version 1 with field mappings or version 2 with a typed expression."));

            var mappings = transformation.FieldMappings ?? Array.Empty<ContractPayloadFieldMappingRequest>();
            if (mappings.Count == 0)
                return Result.Failure(Error.Validation("Transformation must include at least one field mapping."));

            if (mappings.Count > ContractValidation.MaxTransformationMappings)
                return Result.Failure(Error.Validation(
                    $"At most {ContractValidation.MaxTransformationMappings} transformation mappings are allowed."));

            return ValidateTransformationMappings(mappings);
        }

        private static Result ValidateTransformationMappings(IReadOnlyCollection<ContractPayloadFieldMappingRequest> mappings)
        {
            var seenTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in mappings)
            {
                if (!ContractValidation.IsValidFilterSelector(mapping.SourceSelector))
                    return Result.Failure(Error.Validation(
                        $"Invalid transformation source selector '{mapping.SourceSelector}'. Use dot-separated keys only."));

                if (!ContractValidation.IsValidFilterSelector(mapping.TargetSelector))
                    return Result.Failure(Error.Validation(
                        $"Invalid transformation target selector '{mapping.TargetSelector}'. Use dot-separated keys only."));

                if (!seenTargets.Add(mapping.TargetSelector))
                    return Result.Failure(Error.Validation(
                        $"Duplicate transformation target selector '{mapping.TargetSelector}'."));
            }

            return Result.Success();
        }

        private static Result ValidateFilterRules(
            IReadOnlyCollection<ContractFilterRuleRequest> rules,
            string scope)
        {
            foreach (var rule in rules)
            {
                if (!ContractValidation.IsValidFilterSelector(rule.Selector))
                    return Result.Failure(Error.Validation(
                        $"Invalid {scope} filter selector '{rule.Selector}'. Use dot-separated keys only."));

                if (!ContractValidation.IsValidFilterOperator(rule.Operator))
                    return Result.Failure(Error.Validation(
                        $"Invalid {scope} filter operator '{rule.Operator}'."));

                if (ContractValidation.IsSetOperator(rule.Operator))
                {
                    var values = rule.Values ?? Array.Empty<string>();
                    if (values.Count == 0)
                        return Result.Failure(Error.Validation(
                            $"{scope} filter '{rule.Selector}' requires one or more values for operator '{rule.Operator}'."));

                    if (values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > ContractValidation.FilterValueMaxLength))
                        return Result.Failure(Error.Validation(
                            $"{scope} filter '{rule.Selector}' has invalid values."));

                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.Value) || rule.Value.Length > ContractValidation.FilterValueMaxLength)
                    return Result.Failure(Error.Validation(
                        $"{scope} filter '{rule.Selector}' requires a non-empty value for operator '{rule.Operator}'."));

                if (ContractValidation.IsStringOperator(rule.Operator)
                    && (rule.Values?.Count ?? 0) > 0)
                {
                    return Result.Failure(Error.Validation(
                        $"{scope} filter '{rule.Selector}' must not define 'values' for operator '{rule.Operator}'."));
                }
            }

            return Result.Success();
        }
    }
}

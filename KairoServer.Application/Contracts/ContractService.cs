using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Auditing;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Contracts
{
    public sealed class ContractService : IContractService
    {
        public const int MaxPageSize = 100;
        public const int DefaultPageSize = 20;

        private readonly IContractRepository _repository;
        private readonly ICustomerRepository _customers;
        private readonly IEventDefinitionRepository _eventDefinitions;
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDestinationConfigDispatcher _destinations;
        private readonly IAppLogger<ContractService> _logger;

        public ContractService(
            IContractRepository repository,
            ICustomerRepository customers,
            IEventDefinitionRepository eventDefinitions,
            IAuditLogRepository auditLogs,
            IUnitOfWork unitOfWork,
            IDestinationConfigDispatcher destinations,
            IAppLogger<ContractService> logger)
        {
            _repository = repository;
            _customers = customers;
            _eventDefinitions = eventDefinitions;
            _auditLogs = auditLogs;
            _unitOfWork = unitOfWork;
            _destinations = destinations;
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

            var destinationResult = _destinations.Build(request.Configuration);
            if (destinationResult.IsFailure)
                return Result<ContractCreated>.Fail(destinationResult.Errors);

            var customerLookup = await _customers.GetByIdAsync(request.CustomerId, cancellationToken);
            if (customerLookup.IsFailure)
                return Result<ContractCreated>.Fail(customerLookup.Errors);
            if (customerLookup.Value is null)
                return Result.Failure<ContractCreated>(Error.NotFound(
                    $"Customer '{request.CustomerId}' was not found."));

            try
            {
                var now = DateTimeOffset.UtcNow;
                var domainSchemaTargets = new System.Collections.Generic.List<ContractSchemaTarget>(schemaTargets.Count);
                foreach (var target in schemaTargets)
                    domainSchemaTargets.Add(new ContractSchemaTarget(target.EventType, target.Version));

                var contract = Contract.Create(
                    request.CustomerId,
                    request.EventTypes,
                    domainSchemaTargets,
                    request.Filter.ToDomain(),
                    destinationResult.Value,
                    now,
                    request.Transformation.ToDomain(),
                    request.RateLimit.ToDomain(),
                    request.Ordering.ToDomain());

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
            string customerId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(customerId))
                return Result.Failure<PagedResult<ContractView>>(Error.Validation("customerId is required."));

            if (page < 1)
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation("page must be greater than or equal to 1."));

            if (pageSize < 1 || pageSize > MaxPageSize)
                return Result.Failure<PagedResult<ContractView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));

            var result = await _repository.ListByCustomerAsync(customerId, page, pageSize, cancellationToken);
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

                var now = DateTimeOffset.UtcNow;
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

            var destinationResult = _destinations.Build(request.Configuration);
            if (destinationResult.IsFailure)
                return Result<ContractView>.Fail(destinationResult.Errors);

            try
            {
                var lookup = await _repository.GetByIdAsync(id, cancellationToken);
                if (lookup.IsFailure)
                    return Result<ContractView>.Fail(lookup.Errors);

                var contract = lookup.Value;
                if (contract is null)
                    return Result.Failure<ContractView>(Error.NotFound($"Contract '{id}' was not found."));

                var destinationToApply = destinationResult.Value;
                if (rotate)
                {
                    var rotation = _destinations.Rotate(contract.Destination, destinationResult.Value);
                    if (rotation.IsFailure)
                        return Result<ContractView>.Fail(rotation.Errors);

                    destinationToApply = rotation.Value;
                }

                var now = DateTimeOffset.UtcNow;
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
                contract.UpdatedAt));
        }

        private static Result ValidateOrdering(ContractOrderingConfig? ordering)
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

        private static Result ValidateRateLimit(ContractRateLimitConfig? rateLimit)
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

        private static Result ValidateFilter(ContractFilterRequest? filter)
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

        private static Result ValidateTransformation(ContractPayloadTransformationRequest? transformation)
        {
            if (transformation is null)
                return Result.Success();

            if (transformation.Version <= 0)
                return Result.Failure(Error.Validation("Transformation version must be greater than zero."));

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

using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Contracts
{
    public sealed class ContractService : IContractService
    {
        public const int MaxPageSize = 100;
        public const int DefaultPageSize = 20;

        private readonly IContractRepository _repository;
        private readonly ICustomerRepository _customers;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDestinationConfigDispatcher _destinations;
        private readonly IAppLogger<ContractService> _logger;

        public ContractService(
            IContractRepository repository,
            ICustomerRepository customers,
            IUnitOfWork unitOfWork,
            IDestinationConfigDispatcher destinations,
            IAppLogger<ContractService> logger)
        {
            _repository = repository;
            _customers = customers;
            _unitOfWork = unitOfWork;
            _destinations = destinations;
            _logger = logger;
        }

        public async Task<Result<ContractCreated>> CreateAsync(
            CreateContractRequest request,
            CancellationToken cancellationToken)
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
                var contract = Contract.Create(
                    request.CustomerId,
                    request.EventTypes,
                    destinationResult.Value,
                    now);

                _unitOfWork.Begin(true);
                var insert = await _repository.InsertAsync(contract, _unitOfWork, cancellationToken);
                if (insert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractCreated>.Fail(insert.Errors);
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

        public Task<Result<ContractView>> EnableAsync(Guid id, CancellationToken cancellationToken) =>
            ToggleAsync(id, enable: true, cancellationToken);

        public Task<Result<ContractView>> DisableAsync(Guid id, CancellationToken cancellationToken) =>
            ToggleAsync(id, enable: false, cancellationToken);

        public Task<Result<ContractView>> UpdateCredentialsAsync(
            Guid id,
            UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken) =>
            UpsertCredentialsAsync(id, request, "updated", cancellationToken);

        public Task<Result<ContractView>> RotateCredentialsAsync(
            Guid id,
            UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken) =>
            UpsertCredentialsAsync(id, request, "rotated", cancellationToken);

        private async Task<Result<ContractView>> ToggleAsync(
            Guid id,
            bool enable,
            CancellationToken cancellationToken)
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
            CancellationToken cancellationToken)
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

                contract.UpdateDestination(destinationResult.Value, DateTimeOffset.UtcNow);

                _unitOfWork.Begin(true);
                var update = await _repository.UpdateAsync(contract, _unitOfWork, cancellationToken);
                if (update.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<ContractView>.Fail(update.Errors);
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
                ContractMapping.MapDestinationType(contract.Destination.Type),
                redactedConfig.Value,
                contract.CreatedAt,
                contract.UpdatedAt));
        }
    }
}

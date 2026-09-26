using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.Customers;
using NuntivusServer.Domain.Auditing;
using NuntivusServer.Domain.Customers;

namespace NuntivusServer.Application.Customers
{
    public sealed class CustomerService : ICustomerService
    {
        public const int MaxPageSize = 100;
        public const int DefaultPageSize = 20;

        private readonly ICustomerRepository _repository;
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<CustomerService> _logger;

        public CustomerService(
            ICustomerRepository repository,
            IAuditLogRepository auditLogs,
            IUnitOfWork unitOfWork,
            IAppLogger<CustomerService> logger)
        {
            _repository = repository;
            _auditLogs = auditLogs;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<CustomerCreated>> CreateAsync(
            string id,
            string name,
            CancellationToken cancellationToken,
            string? actorId = null)
        {
            if (!CustomerValidation.IsValidId(id))
                return Result.Failure<CustomerCreated>(Error.Validation(
                    "Customer id must be 3-64 chars, lowercase alphanumerics, hyphens or underscores."));

            if (!CustomerValidation.IsValidName(name))
                return Result.Failure<CustomerCreated>(Error.Validation(
                    $"Customer name is required and must be at most {CustomerValidation.NameMaxLength} characters."));

            try
            {
                var now = DateTimeOffset.UtcNow;
                var customer = Customer.Create(id, name, now);

                _unitOfWork.Begin(true);
                var insert = await _repository.InsertAsync(customer, _unitOfWork, cancellationToken);
                if (insert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<CustomerCreated>.Fail(insert.Errors);
                }

                var audit = BuildAudit(
                    action: "created",
                    actorId: actorId,
                    customerId: customer.Id,
                    details: JsonSerializer.Serialize(new
                    {
                        customer.Id,
                        customer.Name,
                        customer.Enabled,
                    }),
                    createdAt: now);
                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<CustomerCreated>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();

                _logger.Information("Customer created {CustomerId} ({Name}).", customer.Id, customer.Name);

                return Result.Success(new CustomerCreated(
                    customer.Id, customer.Name, customer.Enabled, customer.CreatedAt));
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to create customer {CustomerId}.", id);
                return Result.Failure<CustomerCreated>(ex);
            }
        }

        public async Task<Result<CustomerView>> GetByIdAsync(
            string id,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
                return Result.Failure<CustomerView>(Error.Validation("Customer id is required."));

            var result = await _repository.GetByIdAsync(id, cancellationToken);
            if (result.IsFailure)
                return Result<CustomerView>.Fail(result.Errors);

            var customer = result.Value;
            if (customer is null)
                return Result.Failure<CustomerView>(Error.NotFound($"Customer '{id}' was not found."));

            return Result.Success(customer.ToView());
        }

        public async Task<Result<CustomerView>> UpdateAsync(
            string id,
            string name,
            CancellationToken cancellationToken,
            string? actorId = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                return Result.Failure<CustomerView>(Error.Validation("Customer id is required."));

            if (!CustomerValidation.IsValidName(name))
                return Result.Failure<CustomerView>(Error.Validation(
                    $"Customer name is required and must be at most {CustomerValidation.NameMaxLength} characters."));

            try
            {
                var lookup = await _repository.GetByIdAsync(id, cancellationToken);
                if (lookup.IsFailure)
                    return Result<CustomerView>.Fail(lookup.Errors);

                var customer = lookup.Value;
                if (customer is null)
                    return Result.Failure<CustomerView>(Error.NotFound($"Customer '{id}' was not found."));

                var now = DateTimeOffset.UtcNow;
                customer.Rename(name, now);

                _unitOfWork.Begin(true);
                var update = await _repository.UpdateAsync(customer, _unitOfWork, cancellationToken);
                if (update.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<CustomerView>.Fail(update.Errors);
                }

                var audit = BuildAudit(
                    action: "updated",
                    actorId: actorId,
                    customerId: customer.Id,
                    details: JsonSerializer.Serialize(new
                    {
                        customer.Id,
                        customer.Name,
                    }),
                    createdAt: now);

                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<CustomerView>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();
                _logger.Information("Customer updated {CustomerId}.", customer.Id);
                return Result.Success(customer.ToView());
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to update customer {CustomerId}.", id);
                return Result.Failure<CustomerView>(ex);
            }
        }

        public async Task<Result<PagedResult<CustomerView>>> ListAsync(
            CustomerListQuery query,
            CancellationToken cancellationToken)
        {
            if (query is null)
                return Result.Failure<PagedResult<CustomerView>>(
                    Error.Validation("query is required."));

            if (query.Page < 1)
                return Result.Failure<PagedResult<CustomerView>>(
                    Error.Validation("page must be greater than or equal to 1."));

            if (query.PageSize < 1 || query.PageSize > MaxPageSize)
                return Result.Failure<PagedResult<CustomerView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));

            if (query.CreatedFrom.HasValue && query.CreatedTo.HasValue && query.CreatedFrom > query.CreatedTo)
            {
                return Result.Failure<PagedResult<CustomerView>>(
                    Error.Validation("createdFrom must be less than or equal to createdTo."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortBy)
                && !string.Equals(query.SortBy, "id", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "name", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "createdAt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortBy, "updatedAt", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<CustomerView>>(
                    Error.Validation("sortBy must be one of: id, name, createdAt, updatedAt."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortDirection)
                && !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<CustomerView>>(
                    Error.Validation("sortDirection must be one of: asc, desc."));
            }

            var result = await _repository.ListAsync(query, cancellationToken);
            if (result.IsFailure)
                return Result<PagedResult<CustomerView>>.Fail(result.Errors);

            return Result.Success(result.Value.Map(c => c.ToView()));
        }

        public Task<Result<CustomerView>> EnableAsync(string id, CancellationToken cancellationToken, string? actorId = null) =>
            ToggleAsync(id, enable: true, cancellationToken, actorId);

        public Task<Result<CustomerView>> DisableAsync(string id, CancellationToken cancellationToken, string? actorId = null) =>
            ToggleAsync(id, enable: false, cancellationToken, actorId);

        private async Task<Result<CustomerView>> ToggleAsync(
            string id,
            bool enable,
            CancellationToken cancellationToken,
            string? actorId)
        {
            if (string.IsNullOrWhiteSpace(id))
                return Result.Failure<CustomerView>(Error.Validation("Customer id is required."));

            try
            {
                var lookup = await _repository.GetByIdAsync(id, cancellationToken);
                if (lookup.IsFailure)
                    return Result<CustomerView>.Fail(lookup.Errors);

                var customer = lookup.Value;
                if (customer is null)
                    return Result.Failure<CustomerView>(Error.NotFound($"Customer '{id}' was not found."));

                if (customer.Enabled == enable)
                {
                    return Result.Success(customer.ToView());
                }

                var now = DateTimeOffset.UtcNow;
                if (enable) customer.Enable(now); else customer.Disable(now);

                _unitOfWork.Begin(true);
                var update = await _repository.UpdateAsync(customer, _unitOfWork, cancellationToken);
                if (update.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<CustomerView>.Fail(update.Errors);
                }

                var action = enable ? "enabled" : "disabled";
                var audit = BuildAudit(
                    action,
                    actorId,
                    customer.Id,
                    JsonSerializer.Serialize(new
                    {
                        customer.Id,
                        customer.Enabled,
                    }),
                    now);
                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<CustomerView>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();

                _logger.Information(
                    "Customer {Action} {CustomerId}.",
                    enable ? "enabled" : "disabled",
                    customer.Id);

                return Result.Success(customer.ToView());
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to toggle customer {CustomerId} (enable={Enable}).", id, enable);
                return Result.Failure<CustomerView>(ex);
            }
        }

        private static AuditLogEntry BuildAudit(
            string action,
            string? actorId,
            string customerId,
            string details,
            DateTimeOffset createdAt)
        {
            return AuditLogEntry.Create(
                category: "customer",
                action: action,
                actorId: actorId,
                customerId: customerId,
                contractId: null,
                deliveryId: null,
                eventId: null,
                operationId: null,
                details: details,
                createdAt: createdAt,
                expiresAt: createdAt.AddDays(90));
        }
    }
}

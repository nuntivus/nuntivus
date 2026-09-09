using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Customers;
using KairoServer.Domain.Customers;

namespace KairoServer.Application.Customers
{
    public sealed class CustomerService : ICustomerService
    {
        public const int MaxPageSize = 100;
        public const int DefaultPageSize = 20;

        private readonly ICustomerRepository _repository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<CustomerService> _logger;

        public CustomerService(
            ICustomerRepository repository,
            IUnitOfWork unitOfWork,
            IAppLogger<CustomerService> logger)
        {
            _repository = repository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<CustomerCreated>> CreateAsync(
            string id,
            string name,
            CancellationToken cancellationToken)
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

        public async Task<Result<PagedResult<CustomerView>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (page < 1)
                return Result.Failure<PagedResult<CustomerView>>(
                    Error.Validation("page must be greater than or equal to 1."));

            if (pageSize < 1 || pageSize > MaxPageSize)
                return Result.Failure<PagedResult<CustomerView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));

            var result = await _repository.ListAsync(page, pageSize, cancellationToken);
            if (result.IsFailure)
                return Result<PagedResult<CustomerView>>.Fail(result.Errors);

            return Result.Success(result.Value.Map(c => c.ToView()));
        }

        public Task<Result<CustomerView>> EnableAsync(string id, CancellationToken cancellationToken) =>
            ToggleAsync(id, enable: true, cancellationToken);

        public Task<Result<CustomerView>> DisableAsync(string id, CancellationToken cancellationToken) =>
            ToggleAsync(id, enable: false, cancellationToken);

        private async Task<Result<CustomerView>> ToggleAsync(
            string id,
            bool enable,
            CancellationToken cancellationToken)
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

                var now = DateTimeOffset.UtcNow;
                if (enable) customer.Enable(now); else customer.Disable(now);

                _unitOfWork.Begin(true);
                var update = await _repository.UpdateAsync(customer, _unitOfWork, cancellationToken);
                if (update.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<CustomerView>.Fail(update.Errors);
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
    }
}

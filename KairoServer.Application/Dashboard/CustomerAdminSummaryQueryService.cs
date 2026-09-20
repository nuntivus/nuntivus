using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Contracts;
using KairoServer.Application.Customers;
using KairoServer.Application.Deliveries.Health;
using KairoServer.Common;
using KairoServer.Contracts.Dashboard;

namespace KairoServer.Application.Dashboard
{
    public sealed class CustomerAdminSummaryQueryService : ICustomerAdminSummaryQueryService
    {
        private readonly ICustomerService _customers;
        private readonly IContractService _contracts;
        private readonly IDestinationHealthQueryService _destinationHealth;

        public CustomerAdminSummaryQueryService(
            ICustomerService customers,
            IContractService contracts,
            IDestinationHealthQueryService destinationHealth)
        {
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
            _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
            _destinationHealth = destinationHealth ?? throw new ArgumentNullException(nameof(destinationHealth));
        }

        public async Task<Result<CustomerAdminSummaryView>> GetAsync(string customerId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return Result.Failure<CustomerAdminSummaryView>(Error.Validation("customerId is required."));
            }

            var normalizedCustomerId = customerId.Trim();

            var customerResult = await _customers.GetByIdAsync(normalizedCustomerId, cancellationToken);
            if (customerResult.IsFailure)
                return Result<CustomerAdminSummaryView>.Fail(customerResult.Errors);

            var totalContractsResult = await CountContractsAsync(normalizedCustomerId, null, cancellationToken);
            if (totalContractsResult.IsFailure)
                return Result<CustomerAdminSummaryView>.Fail(totalContractsResult.Errors);

            var enabledContractsResult = await CountContractsAsync(normalizedCustomerId, true, cancellationToken);
            if (enabledContractsResult.IsFailure)
                return Result<CustomerAdminSummaryView>.Fail(enabledContractsResult.Errors);

            var disabledContractsResult = await CountContractsAsync(normalizedCustomerId, false, cancellationToken);
            if (disabledContractsResult.IsFailure)
                return Result<CustomerAdminSummaryView>.Fail(disabledContractsResult.Errors);

            var destinationHealthResult = await _destinationHealth.ListByCustomerIdAsync(normalizedCustomerId, cancellationToken);
            if (destinationHealthResult.IsFailure)
                return Result<CustomerAdminSummaryView>.Fail(destinationHealthResult.Errors);

            var healthRows = destinationHealthResult.Value;
            var healthyCount = healthRows.Count(row => row.Status == DestinationHealthStatus.Healthy);
            var degradedCount = healthRows.Count(row => row.Status == DestinationHealthStatus.Degraded);
            var unhealthyCount = healthRows.Count(row => row.Status == DestinationHealthStatus.Unhealthy);

            return Result.Success(new CustomerAdminSummaryView(
                customerResult.Value.Id,
                customerResult.Value.Name,
                customerResult.Value.Enabled,
                totalContractsResult.Value,
                enabledContractsResult.Value,
                disabledContractsResult.Value,
                healthyCount,
                degradedCount,
                unhealthyCount,
                DateTimeOffset.UtcNow));
        }

        private async Task<Result<long>> CountContractsAsync(
            string customerId,
            bool? enabled,
            CancellationToken cancellationToken)
        {
            var result = await _contracts.ListByCustomerAsync(
                new ContractListQuery(
                    customerId,
                    enabled,
                    null,
                    null,
                    null,
                    null,
                    null,
                    1,
                    1),
                cancellationToken);

            if (result.IsFailure)
                return Result<long>.Fail(result.Errors);

            return Result.Success(result.Value.TotalCount);
        }
    }
}

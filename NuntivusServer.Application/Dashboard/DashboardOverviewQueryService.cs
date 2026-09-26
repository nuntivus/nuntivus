using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Customers;
using NuntivusServer.Application.Deliveries.Queries;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Dashboard;
using NuntivusServer.Domain.Deliveries;

namespace NuntivusServer.Application.Dashboard
{
    public sealed class DashboardOverviewQueryService : IDashboardOverviewQueryService
    {
        private readonly ICustomerService _customers;
        private readonly IDeliveryQueryService _deliveries;

        public DashboardOverviewQueryService(
            ICustomerService customers,
            IDeliveryQueryService deliveries)
        {
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
        }

        public async Task<Result<DashboardOverviewView>> GetAsync(CancellationToken cancellationToken)
        {
            var customersResult = await _customers.ListAsync(
                new CustomerListQuery(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    1,
                    1),
                cancellationToken);

            if (customersResult.IsFailure)
                return Result<DashboardOverviewView>.Fail(customersResult.Errors);

            var pendingCountResult = await CountDeliveriesByStatusAsync(DeliveryStatus.Pending, cancellationToken);
            if (pendingCountResult.IsFailure)
                return Result<DashboardOverviewView>.Fail(pendingCountResult.Errors);

            var failedCountResult = await CountDeliveriesByStatusAsync(DeliveryStatus.Failed, cancellationToken);
            if (failedCountResult.IsFailure)
                return Result<DashboardOverviewView>.Fail(failedCountResult.Errors);

            var deadCountResult = await CountDeliveriesByStatusAsync(DeliveryStatus.Dead, cancellationToken);
            if (deadCountResult.IsFailure)
                return Result<DashboardOverviewView>.Fail(deadCountResult.Errors);

            return Result.Success(new DashboardOverviewView(
                DateTimeOffset.UtcNow,
                customersResult.Value.TotalCount,
                pendingCountResult.Value,
                failedCountResult.Value,
                deadCountResult.Value));
        }

        private async Task<Result<long>> CountDeliveriesByStatusAsync(
            DeliveryStatus status,
            CancellationToken cancellationToken)
        {
            var result = await _deliveries.ListAsync(
                new DeliveryListQuery(
                    null,
                    null,
                    null,
                    null,
                    null,
                    status,
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

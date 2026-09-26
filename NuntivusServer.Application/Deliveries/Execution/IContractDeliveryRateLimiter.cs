using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Deliveries.Execution
{
    public interface IContractDeliveryRateLimiter
    {
        Task<Result<DeliveryRateLimitPermit>> TryAcquirePermitAsync(
            Guid contractId,
            ContractRateLimit rateLimit,
            DateTimeOffset now,
            CancellationToken cancellationToken);
    }
}
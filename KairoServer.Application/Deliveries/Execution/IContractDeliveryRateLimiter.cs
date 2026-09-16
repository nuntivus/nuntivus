using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Deliveries.Execution
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
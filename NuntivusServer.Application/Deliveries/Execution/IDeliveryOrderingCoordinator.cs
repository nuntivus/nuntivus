using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Domain.Deliveries;

namespace NuntivusServer.Application.Deliveries.Execution
{
    public interface IDeliveryOrderingCoordinator
    {
        Task<Result<DeliveryOrderingGateDecision>> TryAcquireAsync(
            Delivery delivery,
            CancellationToken cancellationToken);

        Task<Result> RecordOutcomeAsync(
            Delivery delivery,
            CancellationToken cancellationToken);
    }
}

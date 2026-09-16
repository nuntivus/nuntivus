using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Deliveries.Execution
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

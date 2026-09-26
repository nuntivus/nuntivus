using System.Threading;
using System.Threading.Tasks;

namespace NuntivusServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Sole entry point Delivery Core uses to execute a delivery attempt.
    /// Resolves the transport-specific <see cref="IDeliveryExecutor"/> from
    /// the destination type and forwards the request.
    /// </summary>
    public interface IDeliveryExecutorDispatcher
    {
        Task<DeliveryExecutionResult> ExecuteAsync(
            DeliveryExecutionRequest request,
            CancellationToken cancellationToken);
    }
}

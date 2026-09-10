using System.Threading;
using System.Threading.Tasks;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Executes a single delivery attempt for a specific destination transport.
    /// One implementation per <see cref="DestinationType"/>. Implementations
    /// live in the infrastructure layer so the application core stays
    /// protocol-independent.
    /// </summary>
    public interface IDeliveryExecutor
    {
        /// <summary>The destination transport this executor handles.</summary>
        DestinationType DestinationType { get; }

        /// <summary>
        /// Performs the delivery attempt. Must not throw for expected
        /// transport failures — classify them via <see cref="DeliveryExecutionResult.Failure"/>.
        /// </summary>
        Task<DeliveryExecutionResult> ExecuteAsync(
            DeliveryExecutionRequest request,
            CancellationToken cancellationToken);
    }
}

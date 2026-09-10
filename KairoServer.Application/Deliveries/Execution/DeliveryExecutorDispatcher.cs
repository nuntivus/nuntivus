using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Resolves the <see cref="IDeliveryExecutor"/> registered for the
    /// request's <see cref="DestinationType"/> and forwards the call.
    /// </summary>
    public sealed class DeliveryExecutorDispatcher : IDeliveryExecutorDispatcher
    {
        private readonly IReadOnlyDictionary<DestinationType, IDeliveryExecutor> _executors;

        public DeliveryExecutorDispatcher(IEnumerable<IDeliveryExecutor> executors)
        {
            if (executors is null) throw new ArgumentNullException(nameof(executors));

            _executors = executors
                .GroupBy(e => e.DestinationType)
                .ToDictionary(g => g.Key, g =>
                {
                    if (g.Count() > 1)
                    {
                        throw new InvalidOperationException(
                            $"Multiple IDeliveryExecutor implementations registered for {g.Key}.");
                    }
                    return g.Single();
                });
        }

        public Task<DeliveryExecutionResult> ExecuteAsync(
            DeliveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!_executors.TryGetValue(request.Destination.Type, out var executor))
            {
                return Task.FromResult(DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    $"No delivery executor registered for destination type '{request.Destination.Type}'."));
            }

            return executor.ExecuteAsync(request, cancellationToken);
        }
    }
}

using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Infastructure.Health
{
    internal static class DestinationTestRequest
    {
        internal static async Task<Result<string>> SendAsync(IDeliveryExecutor executor, Destination destination,
            string payload, CancellationToken cancellationToken)
        {
            var result = await executor.ExecuteAsync(new DeliveryExecutionRequest(
                Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "connection-test", "connection-test",
                payload, 1, destination), cancellationToken);
            return result.Succeeded
                ? Result.Success($"{destination.Type} destination accepted the test message. A real message was sent; no event or delivery was stored.")
                : Result.Failure<string>(Error.Validation($"{destination.Type} test send failed: {result.Category}. Check the configured credentials, resource and permissions."));
        }
    }
}

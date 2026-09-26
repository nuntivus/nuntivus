using NuntivusServer.Common;

namespace NuntivusServer.Application.Deliveries.Execution
{
    public interface IDeliveryPayloadTransformer
    {
        Result<string> Apply(string payload, string? transformationConfiguration);
    }
}

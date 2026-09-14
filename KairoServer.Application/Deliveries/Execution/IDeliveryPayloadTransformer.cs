using KairoServer.Common;

namespace KairoServer.Application.Deliveries.Execution
{
    public interface IDeliveryPayloadTransformer
    {
        Result<string> Apply(string payload, string? transformationConfiguration);
    }
}

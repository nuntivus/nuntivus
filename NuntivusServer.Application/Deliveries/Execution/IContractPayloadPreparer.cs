using NuntivusServer.Common;

namespace NuntivusServer.Application.Deliveries.Execution
{
    public interface IContractPayloadPreparer
    {
        Task<Result<string>> PrepareAsync(string eventType, string payload, string? transformationConfiguration,
            int? schemaVersion, CancellationToken cancellationToken);
    }
}

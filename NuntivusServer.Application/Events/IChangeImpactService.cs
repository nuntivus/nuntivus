using NuntivusServer.Common;
using NuntivusServer.Contracts.Events;

namespace NuntivusServer.Application.Events
{
    public interface IChangeImpactService
    {
        Task<Result<ChangeImpactReport>> AnalyzeSchemaAsync(string eventType, int version, int page, int pageSize, int sampleSize, CancellationToken cancellationToken);
        Task<Result<ChangeImpactReport>> AnalyzeTransformationAsync(Guid contractId, TransformationImpactRequest request, CancellationToken cancellationToken);
    }
}

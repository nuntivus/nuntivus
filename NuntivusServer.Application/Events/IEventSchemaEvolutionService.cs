using NuntivusServer.Common;
using NuntivusServer.Contracts.Events;

namespace NuntivusServer.Application.Events
{
    public interface IEventSchemaEvolutionService
    {
        Task<Result<SchemaCompatibilityReport>> CompareAsync(string eventType, int version, int? againstVersion, CancellationToken cancellationToken);
        Task<Result<EventSchemaVersionView>> ActivateAsync(string eventType, int version, CancellationToken cancellationToken,
            string? actorId, bool canOverride, SchemaActivationOptions? options);
    }
}

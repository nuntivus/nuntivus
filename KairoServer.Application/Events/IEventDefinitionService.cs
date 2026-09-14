using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Events;

namespace KairoServer.Application.Events
{
    public interface IEventDefinitionService
    {
        Task<Result<EventDefinitionCreated>> CreateAsync(
            string eventType,
            string displayName,
            string? description,
            CancellationToken cancellationToken);

        Task<Result<EventDefinitionView>> GetByEventTypeAsync(
            string eventType,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<EventDefinitionView>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<Result<EventSchemaVersionView>> CreateSchemaDraftAsync(
            string eventType,
            int version,
            string schema,
            SchemaCompatibilityMode compatibilityMode,
            CancellationToken cancellationToken);

        Task<Result<EventSchemaVersionView>> ActivateSchemaVersionAsync(
            string eventType,
            int version,
            CancellationToken cancellationToken);

        Task<Result<EventSchemaVersionView>> DeprecateSchemaVersionAsync(
            string eventType,
            int version,
            CancellationToken cancellationToken);

        Task<Result<EventSchemaVersionView>> GetSchemaVersionAsync(
            string eventType,
            int version,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<EventSchemaVersionView>>> ListSchemaVersionsAsync(
            string eventType,
            int page,
            int pageSize,
            CancellationToken cancellationToken);
    }
}
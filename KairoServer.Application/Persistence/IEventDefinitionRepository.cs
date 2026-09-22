using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Domain.Events;

namespace KairoServer.Application.Persistence
{
    public interface IEventDefinitionRepository
    {
        Task<Result> InsertAsync(
            EventDefinition definition,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result<EventDefinition?>> GetByEventTypeAsync(
            string eventType,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<EventDefinition>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<Result<int>> UpdateDescriptionAsync(
            string eventType,
            string? description,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result> InsertSchemaVersionAsync(
            EventSchemaVersion schemaVersion,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result<EventSchemaVersion?>> GetSchemaVersionAsync(
            string eventType,
            int version,
            CancellationToken cancellationToken);

        Task<Result<PagedResult<EventSchemaVersion>>> ListSchemaVersionsAsync(
            string eventType,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<Result> ActivateSchemaVersionAsync(
            string eventType,
            int version,
            DateTimeOffset activatedAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken);

        Task<Result> DeprecateSchemaVersionAsync(
            string eventType,
            int version,
            DateTimeOffset deprecatedAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken);
    }
}
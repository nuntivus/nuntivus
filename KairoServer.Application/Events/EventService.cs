using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Events;
using KairoServer.Domain.Events;
using KairoServer.Domain.Outbox;

namespace KairoServer.Application.Events
{
    public sealed class EventService : IEventService
    {
        private readonly IEventRepository _eventRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<EventService> _logger;

        public EventService(
            IEventRepository eventRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            IAppLogger<EventService> logger)
        {
            _eventRepository = eventRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<Guid>> PublishAsync(
            string idempotencyKey,
            string type,
            string sourceId,
            string payload,
            DateTimeOffset occurredAt,
            string? metadata,
            CancellationToken cancellationToken)
        {
            _logger.Debug(
                "Handling PublishEvent (idempotencyKey {IdempotencyKey}, type {Type}, source {Source}).",
                idempotencyKey, type, sourceId);

            try
            {
                var now = DateTimeOffset.UtcNow;

                var @event = Event.Create(
                    idempotencyKey,
                    type,
                    sourceId,
                    occurredAt,
                    payload,
                    metadata);

                var eventAccepted = new EventAccepted(
                    @event.Id,
                    @event.Type,
                    @event.SourceId,
                    @event.OccurredAt);

                var outbox = OutboxMessage.Create(
                    @event.Id,
                    OutboxMessageType.EventAccepted,
                    JsonSerializer.Serialize(eventAccepted),
                    now);

                _unitOfWork.Begin(true);

                var eventResult = await _eventRepository.InsertAsync(@event, _unitOfWork, cancellationToken);
                eventResult.EnsureSuccess();

                var outboxResult = await _outboxRepository.InsertAsync(outbox, _unitOfWork, cancellationToken);
                outboxResult.EnsureSuccess();

                _unitOfWork.Commit();

                _logger.Information(
                    "Event accepted {EventId} (type {Type}, source {Source}, outbox {OutboxId}).",
                    @event.Id, @event.Type, @event.SourceId, outbox.Id);

                return Result.Success(@event.Id);
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to publish event (idempotencyKey {IdempotencyKey}, type {Type}).",
                    idempotencyKey, type);
                return Result.Failure<Guid>(ex);
            }
        }
    }
}

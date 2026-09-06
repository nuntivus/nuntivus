using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Events;
using KairoServer.Domain.Events;
using KairoServer.Domain.Outbox;
using KairoServer.Domain.Sources;
using System;
using System.Text.Json;

namespace KairoServer.Application.Events.Pub
{
    public class PublishEventHandler : IPublishEventHandler
    {
        private readonly IEventRepository _eventRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<PublishEventHandler> _logger;

        public PublishEventHandler(
            IEventRepository eventRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            IAppLogger<PublishEventHandler> logger)
        {
            _eventRepository = eventRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<PublishEventResult>> HandleAsync(
            PublishEventCommand command,
            CancellationToken cancellationToken)
        {
            _logger.Debug(
                "Handling PublishEvent (idempotencyKey {IdempotencyKey}, type {Type}, source {Source}).",
                command.IdempotencyKey,
                command.Type,
                command.SourceId);

            try
            {
                var now = DateTimeOffset.UtcNow;

                var @event = Event.Create(
                    command.IdempotencyKey,
                    command.Type,
                    command.SourceId,
                    now,
                    command.Payload,
                    command.Metadata);

                var eventAccepted = new EventAccepted(
                    @event.Id,
                    @event.Type,
                    @event.SourceId,
                    @event.OccurredAt
                );

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
                    @event.Id,
                    @event.Type,
                    @event.SourceId,
                    outbox.Id);

                return Result.Success(new PublishEventResult(@event.Id));
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();

                _logger.Error(
                    ex,
                    "Failed to publish event (idempotencyKey {IdempotencyKey}, type {Type}).",
                    command.IdempotencyKey,
                    command.Type);

                return Result.Failure<PublishEventResult>(ex);
            }
        }

    }
}

using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Contracts.Events;
using KairoServer.Domain.Events;
using KairoServer.Domain.Outbox;
using KairoServer.Domain.Sources;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KairoServer.Application.Events.Pub
{
    public class PublishEventHandler : IPublishEventHandler
    {
        private readonly IEventRepository _eventRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;

        public PublishEventHandler(
            IEventRepository eventRepository, 
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork)
        {
            _eventRepository = eventRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<PublishEventResult>> HandleAsync(
            PublishEventCommand command,
            CancellationToken cancellationToken)
        {
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
                    @event.SourceId.Value(),
                    @event.OccurredAt
                );

                var outbox = OutboxMessage.Create(
                    OutboxMessageType.EventAccepted,
                    JsonSerializer.Serialize(eventAccepted),
                    now);

                _unitOfWork.Begin(true);

                var eventResult = await _eventRepository.InsertAsync(@event, _unitOfWork, cancellationToken);
                eventResult.EnsureSuccess();

                var outboxResult = await _outboxRepository.InsertAsync(outbox, _unitOfWork, cancellationToken);
                outboxResult.EnsureSuccess();

                _unitOfWork.Commit();

                return Result.Success(new PublishEventResult(@event.Id));
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();

                return Result.Failure<PublishEventResult>(ex);
            }
        }

    }
}

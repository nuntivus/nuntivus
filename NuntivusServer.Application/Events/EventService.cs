using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.Events;
using NuntivusServer.Domain.Auditing;
using NuntivusServer.Domain.Events;
using NuntivusServer.Domain.Outbox;

namespace NuntivusServer.Application.Events
{
    public sealed class EventService : IEventService
    {
        private readonly IEventRepository _eventRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<EventService> _logger;

        public EventService(
            IEventRepository eventRepository,
            IOutboxRepository outboxRepository,
            IAuditLogRepository auditLogs,
            IUnitOfWork unitOfWork,
            IAppLogger<EventService> logger)
        {
            _eventRepository = eventRepository;
            _outboxRepository = outboxRepository;
            _auditLogs = auditLogs;
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

                var existing = await _eventRepository.GetByIngestionKeyAsync(
                    sourceId,
                    idempotencyKey,
                    cancellationToken);
                var existingEvent = existing.EnsureValue();
                if (existingEvent is not null)
                {
                    _logger.Information(
                        "Duplicate publish detected for source {Source} and idempotencyKey {IdempotencyKey}; returning existing event {EventId}.",
                        sourceId,
                        idempotencyKey,
                        existingEvent.Id);
                    return Result.Success(existingEvent.Id);
                }

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
                if (eventResult.IsFailure && eventResult.Errors.Any(e => e.Code == "conflict"))
                {
                    _unitOfWork.Rollback();

                    var existingResult = await _eventRepository.GetByIngestionKeyAsync(
                        sourceId,
                        idempotencyKey,
                        cancellationToken);
                    var existingDuplicate = existingResult.EnsureValue();
                    if (existingDuplicate is not null)
                    {
                        _logger.Information(
                            "Duplicate publish detected for source {Source} and idempotencyKey {IdempotencyKey}; returning existing event {EventId}.",
                            sourceId,
                            idempotencyKey,
                            existingDuplicate.Id);
                        return Result.Success(existingDuplicate.Id);
                    }
                }
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

        public async Task<Result<ReplayEventResult>> ReplayAsync(
            Guid eventId,
            string replayKey,
            string? reason,
            CancellationToken cancellationToken,
            string? actorId = null)
        {
            if (eventId == Guid.Empty)
                return Result.Failure<ReplayEventResult>(Error.Validation("eventId is required."));
            if (string.IsNullOrWhiteSpace(replayKey))
                return Result.Failure<ReplayEventResult>(Error.Validation("replayKey is required."));

            replayKey = replayKey.Trim();

            try
            {
                var existing = await _eventRepository.GetReplayAsync(eventId, replayKey, cancellationToken);
                var existingReplay = existing.EnsureValue();
                if (existingReplay is not null)
                {
                    return Result.Success(new ReplayEventResult(
                        existingReplay.SourceEventId,
                        existingReplay.ReplayedEventId,
                        Created: false));
                }

                var sourceResult = await _eventRepository.GetByIdAsync(eventId, cancellationToken);
                var source = sourceResult.EnsureValue();
                if (source is null)
                    return Result.Failure<ReplayEventResult>(Error.NotFound(
                        $"Event '{eventId}' was not found. Replay is only available while source events are retained."));

                var now = DateTimeOffset.UtcNow;
                var replayMetadata = source.Metadata is null
                    ? new EventMetadata()
                    : EventMetadata.Parse(source.Metadata.ToJson());

                replayMetadata["nuntivus.replay.sourceEventId"] = source.Id.ToString("D");
                replayMetadata["nuntivus.replay.key"] = replayKey;
                replayMetadata["nuntivus.replay.requestedAt"] = now.ToString("O");
                if (!string.IsNullOrWhiteSpace(reason))
                    replayMetadata["nuntivus.replay.reason"] = reason.Trim();

                var replayedEvent = Event.Create(
                    idempotencyKey: $"replay:{source.Id:D}:{replayKey}",
                    type: source.Type,
                    sourceId: source.SourceId,
                    occurredAt: source.OccurredAt,
                    payload: source.Payload,
                    metadata: replayMetadata);

                var accepted = new EventAccepted(
                    replayedEvent.Id,
                    replayedEvent.Type,
                    replayedEvent.SourceId,
                    replayedEvent.OccurredAt);

                var replay = new EventReplay(source.Id, replayKey, replayedEvent.Id, now);

                var outbox = OutboxMessage.Create(
                    replayedEvent.Id,
                    OutboxMessageType.EventAccepted,
                    JsonSerializer.Serialize(accepted),
                    now);

                _unitOfWork.Begin(true);

                var eventInsert = await _eventRepository.InsertAsync(replayedEvent, _unitOfWork, cancellationToken);
                eventInsert.EnsureSuccess();

                var replayInsert = await _eventRepository.InsertReplayAsync(replay, _unitOfWork, cancellationToken);
                if (replayInsert.IsFailure && replayInsert.Errors.Any(e => e.Code == "conflict"))
                {
                    _unitOfWork.Rollback();
                    var winner = await _eventRepository.GetReplayAsync(eventId, replayKey, cancellationToken);
                    var winningReplay = winner.EnsureValue();
                    if (winningReplay is not null)
                    {
                        return Result.Success(new ReplayEventResult(
                            winningReplay.SourceEventId,
                            winningReplay.ReplayedEventId,
                            Created: false));
                    }
                }
                replayInsert.EnsureSuccess();

                var outboxInsert = await _outboxRepository.InsertAsync(outbox, _unitOfWork, cancellationToken);
                outboxInsert.EnsureSuccess();

                var audit = AuditLogEntry.Create(
                    category: "event",
                    action: "replayed",
                    actorId: actorId,
                    customerId: null,
                    contractId: null,
                    deliveryId: null,
                    eventId: source.Id,
                    operationId: null,
                    details: JsonSerializer.Serialize(new
                    {
                        sourceEventId = source.Id,
                        replayedEventId = replayedEvent.Id,
                        replayKey,
                        reason,
                    }),
                    createdAt: now,
                    expiresAt: now.AddDays(90));
                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                auditInsert.EnsureSuccess();

                _unitOfWork.Commit();

                _logger.Information(
                    "Event replay accepted (source {SourceEventId}, replayed {ReplayedEventId}, replayKey {ReplayKey}).",
                    source.Id,
                    replayedEvent.Id,
                    replayKey);

                return Result.Success(new ReplayEventResult(source.Id, replayedEvent.Id, Created: true));
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex,
                    "Failed to replay event {EventId} with replayKey {ReplayKey}.",
                    eventId,
                    replayKey);
                return Result.Failure<ReplayEventResult>(ex);
            }
        }
    }
}

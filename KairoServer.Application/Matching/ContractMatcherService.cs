using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Deliveries;
using KairoServer.Contracts.Events;
using KairoServer.Domain.Deliveries;
using KairoServer.Domain.Outbox;

namespace KairoServer.Application.Matching
{
    public sealed class ContractMatcherService : IContractMatcherService
    {
        private readonly IEventRepository _events;
        private readonly IContractRepository _contracts;
        private readonly IDeliveryRepository _deliveries;
        private readonly IOutboxRepository _outbox;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<ContractMatcherService> _logger;

        public ContractMatcherService(
            IEventRepository events,
            IContractRepository contracts,
            IDeliveryRepository deliveries,
            IOutboxRepository outbox,
            IUnitOfWork unitOfWork,
            IAppLogger<ContractMatcherService> logger)
        {
            _events = events;
            _contracts = contracts;
            _deliveries = deliveries;
            _outbox = outbox;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<MatchResult>> MatchAsync(
            EventAccepted accepted,
            CancellationToken cancellationToken)
        {
            if (accepted is null)
                return Result.Failure<MatchResult>("matcher.invalid_message", "EventAccepted payload is null.");

            _logger.Debug(
                "Matching event {EventId} of type {Type}.",
                accepted.EventId, accepted.EventType);

            try
            {
                var eventResult = await _events.GetByIdAsync(accepted.EventId, cancellationToken);
                var authoritative = eventResult.EnsureValue();

                if (authoritative is null)
                {
                    // Event not (yet) in the store — treat as transient so JetStream
                    // will redeliver. The outbox and the accepted-event share a
                    // transaction, so once the outbox row is visible the event
                    // must be visible too; a miss here means a rare read-after-write
                    // race we want to retry.
                    _logger.Warning(
                        "Event {EventId} not found while matching (transient).",
                        accepted.EventId);
                    return Result.Failure<MatchResult>(
                        Error.NotFound($"Event '{accepted.EventId}' was not found."));
                }

                var matchesResult = await _contracts.ListEnabledMatchingAsync(
                    authoritative.Type, cancellationToken);
                var matches = matchesResult.EnsureValue();

                if (matches.Count == 0)
                {
                    _logger.Information(
                        "No enabled contracts matched event {EventId} of type {Type}.",
                        authoritative.Id, authoritative.Type);
                    return Result.Success(new MatchResult(0, 0));
                }

                var now = DateTimeOffset.UtcNow;
                var deliveries = new List<Delivery>(matches.Count);
                foreach (var match in matches)
                {
                    deliveries.Add(Delivery.CreatePending(
                        authoritative.Id,
                        match.ContractId,
                        match.CustomerId,
                        authoritative.Type,
                        authoritative.Payload,
                        match.Destination,
                        now));
                }

                _unitOfWork.Begin(true);

                var insertResult = await _deliveries.InsertPendingAsync(
                    deliveries, _unitOfWork, cancellationToken);
                var inserted = insertResult.EnsureValue();

                foreach (var delivery in deliveries)
                {
                    var ready = new DeliveryReady(
                        delivery.Id,
                        delivery.EventId,
                        delivery.ContractId,
                        delivery.CustomerId,
                        delivery.EventType,
                        delivery.Payload,
                        authoritative.OccurredAt);

                    var outboxMessage = OutboxMessage.Create(
                        delivery.EventId,
                        OutboxMessageType.DeliveryReady,
                        JsonSerializer.Serialize(ready),
                        now);

                    var outboxResult = await _outbox.InsertAsync(
                        outboxMessage, _unitOfWork, cancellationToken);
                    outboxResult.EnsureSuccess();
                }

                _unitOfWork.Commit();

                _logger.Information(
                    "Matched event {EventId} (type {Type}): {MatchedCount} contract(s), {InsertedCount} delivery row(s) inserted.",
                    authoritative.Id, authoritative.Type, matches.Count, inserted);

                return Result.Success(new MatchResult(matches.Count, inserted));
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex,
                    "Failed to match event {EventId} of type {Type}.",
                    accepted.EventId, accepted.EventType);
                return Result.Failure<MatchResult>(ex);
            }
        }
    }
}

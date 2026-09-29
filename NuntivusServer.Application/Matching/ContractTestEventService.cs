using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Contracts.Deliveries;
using NuntivusServer.Domain.Auditing;
using NuntivusServer.Domain.Events;
using NuntivusServer.Domain.Deliveries;
using NuntivusServer.Domain.Outbox;

namespace NuntivusServer.Application.Matching
{
    public sealed class ContractTestEventService : IContractTestEventService
    {
        private readonly IContractRepository _contracts;
        private readonly IEventRepository _events;
        private readonly IDeliveryRepository _deliveries;
        private readonly IDeliveryFactory _deliveryFactory;
        private readonly IOutboxRepository _outbox;
        private readonly IAuditLogRepository _auditLogs;
        private readonly IContractFilterEvaluator _filterEvaluator;
        private readonly IUnitOfWork _unitOfWork;

        public ContractTestEventService(
            IContractRepository contracts,
            IEventRepository events,
            IDeliveryRepository deliveries,
            IDeliveryFactory deliveryFactory,
            IOutboxRepository outbox,
            IAuditLogRepository auditLogs,
            IContractFilterEvaluator filterEvaluator,
            IUnitOfWork unitOfWork)
        {
            _contracts = contracts;
            _events = events;
            _deliveries = deliveries;
            _deliveryFactory = deliveryFactory;
            _outbox = outbox;
            _auditLogs = auditLogs;
            _filterEvaluator = filterEvaluator;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<ContractTestPreviewView>> PreviewAsync(
            Guid contractId,
            ContractTestEventRequest request,
            CancellationToken cancellationToken)
        {
            var evaluation = await EvaluateAsync(contractId, request, cancellationToken);
            return evaluation.IsFailure
                ? Result<ContractTestPreviewView>.Fail(evaluation.Errors)
                : Result.Success(evaluation.Value.Preview);
        }

        public async Task<Result<ContractTestSendView>> SendAsync(
            Guid contractId,
            ContractTestEventRequest request,
            string actorId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                return Result.Failure<ContractTestSendView>(Error.Unauthorized("Operator identity is required."));

            var evaluation = await EvaluateAsync(contractId, request, cancellationToken);
            if (evaluation.IsFailure)
                return Result<ContractTestSendView>.Fail(evaluation.Errors);

            var (preview, match, @event) = evaluation.Value;
            if (!preview.Matches || match is null)
                return Result.Failure<ContractTestSendView>(Error.Conflict(preview.Reason));

            try
            {
                var now = DateTimeOffset.UtcNow;
                var delivery = _deliveryFactory.CreatePending(
                    @event.Id,
                    match.ContractId,
                    match.CustomerId,
                    @event.Type,
                    @event.Payload,
                    match.Destination,
                    now,
                    match.Transformation is null ? null : JsonSerializer.Serialize(match.Transformation),
                    match.RateLimit,
                    match.Ordering,
                    match.AuthenticationReference,
                    match.AuthenticationVersion,
                    match.TransformationVersion,
                    match.SchemaVersion);

                _unitOfWork.Begin(true);
                (await _events.InsertAsync(@event, _unitOfWork, cancellationToken)).EnsureSuccess();

                var inserted = (await _deliveries.InsertPendingAsync(
                    new[] { delivery }, _unitOfWork, cancellationToken)).EnsureValue();
                if (inserted != 1)
                {
                    _unitOfWork.Rollback();
                    return Result.Failure<ContractTestSendView>(Error.Conflict("Test delivery could not be created."));
                }

                var ready = new DeliveryReady(
                    delivery.Id, @event.Id, match.ContractId, match.CustomerId,
                    @event.Type, @event.Payload, @event.OccurredAt);
                (await _outbox.InsertAsync(
                    OutboxMessage.Create(@event.Id, OutboxMessageType.DeliveryReady,
                        JsonSerializer.Serialize(ready), now),
                    _unitOfWork, cancellationToken)).EnsureSuccess();

                var response = new ContractTestSendView(@event.Id, delivery.Id);
                var audit = AuditLogEntry.Create(
                    "contract", "test_event_sent", actorId, match.CustomerId,
                    match.ContractId, delivery.Id, @event.Id, null,
                    JsonSerializer.Serialize(new { Request = request, Response = response }),
                    now, now.AddDays(90));
                (await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken)).EnsureValue();

                _unitOfWork.Commit();
                return Result.Success(response);
            }
            catch (ResultFailureException ex)
            {
                _unitOfWork.Rollback();
                return Result<ContractTestSendView>.Fail(ex.Errors);
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                return Result.Failure<ContractTestSendView>(ex);
            }
        }

        private async Task<Result<Evaluation>> EvaluateAsync(
            Guid contractId,
            ContractTestEventRequest request,
            CancellationToken cancellationToken)
        {
            if (contractId == Guid.Empty)
                return Result.Failure<Evaluation>(Error.Validation("Contract id is required."));
            if (request is null)
                return Result.Failure<Evaluation>(Error.Validation("Request body is required."));
            if (!ContractValidation.IsValidEventType(request.Type))
                return Result.Failure<Evaluation>(Error.Validation("A valid event type is required."));
            if (request.Payload.ValueKind != JsonValueKind.Object)
                return Result.Failure<Evaluation>(Error.Validation("payload must be a JSON object."));
            if (request.Metadata.HasValue && request.Metadata.Value.ValueKind != JsonValueKind.Object)
                return Result.Failure<Evaluation>(Error.Validation("metadata must be a JSON object of strings."));

            var metadata = new EventMetadata();
            if (request.Metadata.HasValue)
            {
                foreach (var property in request.Metadata.Value.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String)
                        return Result.Failure<Evaluation>(Error.Validation("metadata values must be strings."));
                    metadata[property.Name] = property.Value.GetString()!;
                }
            }

            metadata["nuntivus.test"] = "true";
            metadata["nuntivus.test.contractId"] = contractId.ToString("D");
            var lookup = await _contracts.GetByIdAsync(contractId, cancellationToken);
            if (lookup.IsFailure)
                return Result<Evaluation>.Fail(lookup.Errors);
            var contract = lookup.Value;
            if (contract is null)
                return Result.Failure<Evaluation>(Error.NotFound($"Contract '{contractId}' was not found."));

            if (contract.SourceIds.Count == 0)
                return Result.Failure<Evaluation>(Error.Validation("Contract must have at least one Source."));

            var @event = Event.Create(
                Guid.NewGuid().ToString("N"), request.Type.Trim(), contract.SourceIds.First().ToString("D"),
                DateTimeOffset.UtcNow, request.Payload.GetRawText(), metadata);

            if (!contract.Enabled)
                return Result.Success(new Evaluation(new(false, "Contract is disabled."), null, @event));
            if (!contract.EventTypes.Contains(@event.Type, StringComparer.Ordinal))
                return Result.Success(new Evaluation(new(false, "Contract does not subscribe to this event type."), null, @event));

            var matches = await _contracts.ListEnabledMatchingAsync(@event.Type, @event.SourceId, cancellationToken);
            if (matches.IsFailure)
                return Result<Evaluation>.Fail(matches.Errors);
            var match = matches.Value.FirstOrDefault(item => item.ContractId == contractId);
            if (match is null)
                return Result.Success(new Evaluation(new(false, "Contract or customer is disabled."), null, @event));

            var filter = _filterEvaluator.Evaluate(match.Filter, @event);
            if (filter.IsFailure)
                return Result<Evaluation>.Fail(filter.Errors);
            return Result.Success(new Evaluation(
                filter.Value
                    ? new ContractTestPreviewView(true, "Contract matches this event. Sending will deliver to this contract only.")
                    : new ContractTestPreviewView(false, "Event does not satisfy the contract filter."),
                match, @event));
        }

        private sealed record Evaluation(ContractTestPreviewView Preview, ContractMatch? Match, Event Event);
    }
}

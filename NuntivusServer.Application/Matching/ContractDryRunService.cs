using System.Text.Json;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Events;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Domain.Events;

namespace NuntivusServer.Application.Matching
{
    public sealed class ContractDryRunService(IContractRepository contracts, ICustomerRepository customers,
        ISourceRepository sources, IDestinationRepository destinations, IEventDefinitionRepository schemas,
        IEventSchemaValidator validator, IContractFilterEvaluator filters, IContractPayloadPreparer preparer,
        IDestinationConfigDispatcher destinationConfigs) : IContractDryRunService
    {
        public async Task<Result<ContractDryRunReport>> RunAsync(Guid? contractId, ContractDryRunRequest request, CancellationToken cancellationToken)
        {
            if (!EventDefinitionValidation.IsValidEventType(request.EventType) || request.SourceId == Guid.Empty
                || request.Payload.ValueKind != JsonValueKind.Object || request.Payload.GetRawText().Length > TypedPayloadTransformation.MaxLength)
                return Result.Failure<ContractDryRunReport>(Error.Validation("A valid event type, Source ID and object payload of at most 1,000,000 characters are required."));
            Contract? saved = null;
            if (contractId.HasValue)
            {
                var lookup = await contracts.GetByIdAsync(contractId.Value, cancellationToken);
                if (lookup.IsFailure) return Result<ContractDryRunReport>.Fail(lookup.Errors);
                saved = lookup.Value;
                if (saved is null) return Result.Failure<ContractDryRunReport>(Error.NotFound("Contract was not found."));
            }
            var draft = request.Draft;
            if (draft is null && saved is null)
                return Result.Failure<ContractDryRunReport>(Error.Validation("Provide a saved contract or a complete draft."));
            var customerId = draft?.CustomerId ?? saved!.CustomerId;
            if (saved is not null && draft is not null && draft.CustomerId != saved.CustomerId)
                return Result.Failure<ContractDryRunReport>(Error.Validation("A draft cannot change the saved contract's customer."));
            var stages = new List<ContractDryRunStage>();
            void Stage(string name, bool pass, string message) => stages.Add(new(name, pass ? "Passed" : "Failed", message));
            var customer = await customers.GetByIdAsync(customerId, cancellationToken);
            if (customer.IsFailure) return Result<ContractDryRunReport>.Fail(customer.Errors);
            Stage("customer", customer.Value?.Enabled == true, customer.Value?.Enabled == true ? "Customer is enabled." : "Customer is missing or disabled.");
            var source = await sources.GetByIdAsync(request.SourceId.ToString("D"), cancellationToken);
            if (source.IsFailure) return Result<ContractDryRunReport>.Fail(source.Errors);
            var linkedSources = draft?.SourceIds ?? saved?.SourceIds ?? [];
            Stage("source", source.Value?.Enabled == true && linkedSources.Contains(request.SourceId),
                "Source must exist, be enabled and be linked by the proposed contract.");
            var enabled = draft is null ? saved!.Enabled : request.DraftEnabled;
            var eventTypes = draft?.EventTypes ?? saved!.EventTypes;
            Stage("routing", enabled && eventTypes.Contains(request.EventType), "Contract must be enabled and subscribe to this event type.");
            var active = await schemas.GetActiveSchemaVersionAsync(request.EventType, cancellationToken);
            if (active.IsFailure) return Result<ContractDryRunReport>.Fail(active.Errors);
            var input = active.Value is null ? Result.Success() : validator.ValidatePayload(active.Value.Schema, request.Payload.GetRawText());
            Stage("inputSchema", input.IsSuccess, active.Value is null ? "No active input schema; ingestion is allowed."
                : input.IsSuccess ? "Payload satisfies the active input schema." : "Payload does not satisfy the active input schema.");

            var filterValidation = draft is null ? Result.Success() : ContractService.ValidateFilter(draft.Filter);
            var transformValidation = draft is null ? Result.Success() : ContractService.ValidateTransformation(draft.Transformation);
            var optionValidation = draft is null ? Result.Success() : ContractService.ValidateRateLimit(draft.RateLimit);
            if (optionValidation.IsSuccess && draft is not null) optionValidation = ContractService.ValidateOrdering(draft.Ordering);
            Stage("configuration", filterValidation.IsSuccess && transformValidation.IsSuccess && optionValidation.IsSuccess,
                "Filter, transformation, rate-limit and ordering configuration must satisfy contract-save validation.");
            var filter = draft is null ? saved!.Filter : filterValidation.IsSuccess ? draft.Filter.ToDomain() : null;
            var transformation = draft is null ? saved!.Transformation : transformValidation.IsSuccess ? draft.Transformation.ToDomain() : null;
            var metadata = new EventMetadata();
            if (request.Metadata is not null)
            {
                if (request.Metadata.Count > 100 || request.Metadata.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null))
                    return Result.Failure<ContractDryRunReport>(Error.Validation("Metadata must contain at most 100 named string values."));
                foreach (var entry in request.Metadata) metadata[entry.Key] = entry.Value;
            }
            var sample = Event.Create("dry-run", request.EventType, request.SourceId.ToString("D"), DateTimeOffset.UtcNow, request.Payload.GetRawText(), metadata);
            var matched = filterValidation.IsSuccess ? filters.Evaluate(filter, sample) : Result.Failure<bool>(Error.Validation("Invalid filter."));
            Stage("filter", matched.IsSuccess && matched.Value, matched.IsSuccess && matched.Value ? "Payload and metadata satisfy the contract filter." : "Contract filter rejects the event or is invalid.");
            if (draft?.SchemaTargets is { } draftTargets && (draftTargets.Any(target => !ContractValidation.IsValidEventType(target.EventType)
                || target.Version < 1 || !eventTypes.Contains(target.EventType)) || draftTargets.Select(target => target.EventType).Distinct().Count() != draftTargets.Count))
                return Result.Failure<ContractDryRunReport>(Error.Validation("Schema targets must be unique subscribed event types with positive versions."));
            var targets = draft?.SchemaTargets?.Select(target => new ContractSchemaTarget(target.EventType, target.Version)).ToArray()
                ?? (draft is null ? saved!.SchemaTargets.ToArray() : []);
            var selected = targets.Where(target => target.EventType == request.EventType).ToArray();
            var outputVersion = selected.FirstOrDefault()?.Version;
            JsonElement? output = null;
            if (transformValidation.IsSuccess && selected.Length <= 1)
            {
                var prepared = await preparer.PrepareAsync(request.EventType, sample.Payload,
                    transformation is null ? null : JsonSerializer.Serialize(transformation), outputVersion, cancellationToken);
                Stage("outputPreparation", prepared.IsSuccess, prepared.IsSuccess ? "Transformation and selected output-schema validation passed." : "Transformation or output-schema validation failed.");
                if (prepared.IsSuccess)
                {
                    using var document = JsonDocument.Parse(prepared.Value);
                    output = document.RootElement.Clone();
                }
            }
            else Stage("outputPreparation", false, "Transformation configuration or duplicate schema targets prevent output preparation.");
            var destinationResults = new List<ContractDryRunDestination>();
            if (draft is null)
            {
                foreach (var binding in saved!.Destinations)
                    destinationResults.Add(new(binding.Id, binding.DestinationId, binding.Destination.Type.ToString(), binding.DeliveryEnabled,
                        "NotConnected", "Saved validated configuration; connectivity and credentials are not exercised."));
            }
            else if (draft.DestinationIds is { Count: > 0 })
            {
                foreach (var id in draft.DestinationIds.Distinct().Take(101))
                {
                    var found = await destinations.GetByIdAsync(id, cancellationToken);
                    if (found.IsFailure) return Result<ContractDryRunReport>.Fail(found.Errors);
                    destinationResults.Add(new(null, id, found.Value?.Destination.Type.ToString() ?? "Unknown", found.Value?.Enabled == true,
                        found.Value is null ? "Invalid" : "NotConnected", "Referenced destination must exist and be enabled; no connection attempt is made."));
                }
            }
            else
            {
                var built = destinationConfigs.Build(draft.Configuration);
                destinationResults.Add(new(null, null, built.IsSuccess ? built.Value.Type.ToString() : "Unknown", true,
                    built.IsSuccess ? "NotConnected" : "Invalid", built.IsSuccess ? "Configuration validated locally; connectivity is not tested." : "Destination configuration is invalid."));
            }
            Stage("destinations", destinationResults.Count is > 0 and <= 100 && destinationResults.Any(item => item.Enabled)
                && destinationResults.All(item => item.Status != "Invalid"), "At least one enabled valid destination is required. No connection tests or delivery executors run.");
            stages.Add(new("runtimeControls", "NotSimulated", "Rate limits, ordering leases, retries, source authentication and destination reachability are runtime concerns and are not reserved or exercised."));
            return Result.Success(new ContractDryRunReport(stages.All(stage => stage.Status != "Failed"), saved?.Id, customerId,
                active.Value?.Version, outputVersion, transformation?.Identity, output, stages, destinationResults,
                "Read-only point-in-time simulation. No events, deliveries, audit entries, outbox records, network calls or runtime reservations are created. A successful dry run does not guarantee delivery."));
        }
    }
}

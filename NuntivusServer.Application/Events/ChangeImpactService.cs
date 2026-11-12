using System.Text.Json;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Events.Queries;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Events;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Events
{
    public sealed class ChangeImpactService(IEventDefinitionRepository schemas, IContractRepository contracts,
        IEventRepository events, IEventSchemaValidator validator, ISchemaCompatibilityAnalyzer analyzer,
        IContractPayloadPreparer preparer) : IChangeImpactService
    {
        private const string EvidenceNotice = "Read-only point-in-time analysis. Sample results are evidence, not proof of compatibility. Sources, filters and enabled states affect actual routing. Queued deliveries retain their existing snapshots.";

        public async Task<Result<ChangeImpactReport>> AnalyzeSchemaAsync(string eventType, int version, int page, int pageSize, int sampleSize, CancellationToken cancellationToken)
        {
            if (!EventDefinitionValidation.IsValidEventType(eventType) || version < 1 || page < 1 || pageSize is < 1 or > 100 || sampleSize is < 0 or > 20)
                return Result.Failure<ChangeImpactReport>(Error.Validation("Use a valid event type/version, positive page, pageSize 1–100 and sampleSize 0–20."));
            var proposed = await schemas.GetSchemaVersionAsync(eventType, version, cancellationToken);
            if (proposed.IsFailure) return Result<ChangeImpactReport>.Fail(proposed.Errors);
            if (proposed.Value is null) return Result.Failure<ChangeImpactReport>(Error.NotFound("Proposed schema version was not found."));
            var valid = validator.ValidateSchema(proposed.Value.Schema);
            if (valid.IsFailure) return Result<ChangeImpactReport>.Fail(valid.Errors);
            var baseline = await schemas.GetActiveSchemaVersionAsync(eventType, cancellationToken);
            if (baseline.IsFailure) return Result<ChangeImpactReport>.Fail(baseline.Errors);
            var listed = await contracts.ListAsync(new AdminContractListQuery(null, null, null, eventType, null, null, null, "updatedAt", "desc", page, pageSize), cancellationToken);
            if (listed.IsFailure) return Result<ChangeImpactReport>.Fail(listed.Errors);
            var samples = await LoadSamples(eventType, sampleSize, cancellationToken);
            if (samples.IsFailure) return Result<ChangeImpactReport>.Fail(samples.Errors);
            var results = new List<ContractChangeImpact>();
            foreach (var contract in listed.Value.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sampled = samples.Value.Where(sample => contract.SourceIds.Any(id => id.ToString("D") == sample.Source)).Select(sample =>
                {
                    var accepted = validator.ValidatePayload(proposed.Value.Schema, sample.Payload);
                    return new ImpactSample(sample.Id, accepted.IsSuccess, accepted.Errors.Select(error => error.Path ?? error.Message).Take(20).ToArray());
                }).ToArray();
                var comparison = baseline.Value is null ? null : analyzer.Compare(eventType, version, baseline.Value.Version,
                    baseline.Value.Schema, proposed.Value.Schema, (NuntivusServer.Contracts.Events.SchemaCompatibilityMode)proposed.Value.CompatibilityMode);
                var hasTransformation = contract.Transformation is not null;
                var status = sampled.Any(sample => !sample.Accepted) || comparison?.Status == SchemaCompatibilityStatus.Breaking ? "Breaking"
                    : hasTransformation || comparison?.Status == SchemaCompatibilityStatus.Indeterminate ? "ReviewRequired"
                    : "Compatible";
                var reason = hasTransformation
                    ? "Input acceptance is compared separately. This contract has a transformation; static downstream compatibility requires review. Its pinned output schema is unchanged."
                    : "Compared proposed input acceptance with the active input schema. Pinned contract output versions are unchanged; sample acceptance does not prove downstream safety.";
                results.Add(ToImpact(contract, eventType, status, reason, sampled));
            }
            return Result.Success(new ChangeImpactReport(eventType, version, null, null, DateTimeOffset.UtcNow, page, pageSize,
                listed.Value.TotalCount, results, EvidenceNotice));
        }

        public async Task<Result<ChangeImpactReport>> AnalyzeTransformationAsync(Guid contractId, TransformationImpactRequest request, CancellationToken cancellationToken)
        {
            if (contractId == Guid.Empty || !EventDefinitionValidation.IsValidEventType(request.EventType) || request.SampleSize is < 0 or > 20)
                return Result.Failure<ChangeImpactReport>(Error.Validation("A contract, valid event type and sampleSize 0–20 are required."));
            var found = await contracts.GetByIdAsync(contractId, cancellationToken);
            if (found.IsFailure) return Result<ChangeImpactReport>.Fail(found.Errors);
            var contract = found.Value;
            if (contract is null) return Result.Failure<ChangeImpactReport>(Error.NotFound("Contract was not found."));
            if (!contract.EventTypes.Contains(request.EventType)) return Result.Failure<ChangeImpactReport>(Error.Validation("Event type is not subscribed by this contract."));
            var validation = ContractService.ValidateTransformation(request.Transformation);
            if (validation.IsFailure) return Result<ChangeImpactReport>.Fail(validation.Errors);
            var proposed = request.Transformation.ToDomain();
            var configuration = proposed is null ? null : JsonSerializer.Serialize(proposed);
            var samples = await LoadSamples(request.EventType, request.SampleSize, cancellationToken);
            if (samples.IsFailure) return Result<ChangeImpactReport>.Fail(samples.Errors);
            var outputVersion = contract.SchemaTargets.FirstOrDefault(target => target.EventType == request.EventType)?.Version;
            var findings = new List<ImpactSample>();
            foreach (var sample in samples.Value.Where(sample => contract.SourceIds.Any(id => id.ToString("D") == sample.Source)))
            {
                var prepared = await preparer.PrepareAsync(request.EventType, sample.Payload, configuration, outputVersion, cancellationToken);
                findings.Add(new ImpactSample(sample.Id, prepared.IsSuccess, prepared.Errors.Select(error => error.Path ?? error.Message).Take(20).ToArray()));
            }
            var unchanged = proposed?.Identity == contract.Transformation?.Identity;
            var status = findings.Any(sample => !sample.Accepted) ? "Breaking" : unchanged ? "Compatible" : "ReviewRequired";
            var result = ToImpact(contract, request.EventType, status,
                unchanged ? "Transformation content is unchanged. Sample results remain evidence only."
                    : "Transformation changes cannot be proven safe from samples. Review consumer expectations even when all sampled outputs validate.", findings);
            return Result.Success(new ChangeImpactReport(request.EventType, null, contractId, proposed?.Identity, DateTimeOffset.UtcNow, 1, 1, 1, [result], EvidenceNotice));
        }

        private async Task<Result<IReadOnlyList<EventQueryView>>> LoadSamples(string eventType, int size, CancellationToken cancellationToken)
        {
            if (size == 0) return Result.Success<IReadOnlyList<EventQueryView>>([]);
            var result = await events.ListQueryAsync(new EventListQuery(eventType, null, null, null, null, null, "receivedAt", "desc", 1, size), cancellationToken);
            return result.IsFailure ? Result<IReadOnlyList<EventQueryView>>.Fail(result.Errors) : Result.Success(result.Value.Items);
        }

        private static ContractChangeImpact ToImpact(Contract contract, string eventType, string status, string reason, IReadOnlyList<ImpactSample> samples) =>
            new(contract.Id, contract.CustomerId, contract.Name, contract.Enabled, status, reason,
                contract.SchemaTargets.FirstOrDefault(target => target.EventType == eventType)?.Version, contract.Transformation?.Identity,
                contract.Destinations.Select(binding => new ImpactBinding(binding.Id, binding.DestinationId, binding.Destination.Type.ToString(), binding.DeliveryEnabled)).ToArray(), samples);
    }
}

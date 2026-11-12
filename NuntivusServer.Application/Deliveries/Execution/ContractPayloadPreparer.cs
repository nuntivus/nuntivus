using NuntivusServer.Application.Events;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Deliveries.Execution
{
    public sealed class ContractPayloadPreparer(
        IDeliveryPayloadTransformer transformer,
        IEventDefinitionRepository schemas,
        IEventSchemaValidator validator) : IContractPayloadPreparer
    {
        public async Task<Result<string>> PrepareAsync(string eventType, string payload, string? transformationConfiguration,
            int? schemaVersion, CancellationToken cancellationToken)
        {
            var transformed = transformer.Apply(payload, transformationConfiguration);
            if (transformed.IsFailure || schemaVersion is null) return transformed;
            var schema = await schemas.GetSchemaVersionAsync(eventType, schemaVersion.Value, cancellationToken);
            if (schema.IsFailure) return Result<string>.Fail(schema.Errors);
            if (schema.Value is null)
                return Result.Failure<string>(Error.Validation("The snapshotted output schema version does not exist."));
            var validation = validator.ValidatePayload(schema.Value.Schema, transformed.Value);
            return validation.IsFailure ? Result<string>.Fail(validation.Errors) : transformed;
        }
    }
}

using NuntivusServer.Contracts.Events;

namespace NuntivusServer.Api.Controllers.Events
{
    public sealed record CreateEventSchemaVersionRequest(
        int Version,
        string Schema,
        SchemaCompatibilityMode CompatibilityMode);
}
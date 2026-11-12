using NuntivusServer.Contracts.Events;

namespace NuntivusServer.Application.Events
{
    public interface ISchemaCompatibilityAnalyzer
    {
        SchemaCompatibilityReport Compare(string eventType, int version, int againstVersion,
            string previousSchema, string proposedSchema, SchemaCompatibilityMode mode);
    }
}

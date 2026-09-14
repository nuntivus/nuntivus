using System;

namespace KairoServer.Contracts.Events
{
    public sealed record EventSchemaVersionView(
        string EventType,
        int Version,
        string Schema,
        SchemaCompatibilityMode CompatibilityMode,
        EventSchemaState State,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ActivatedAt,
        DateTimeOffset? DeprecatedAt);
}
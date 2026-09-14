using System;

namespace KairoServer.Contracts.Events
{
    public sealed record EventDefinitionCreated(
        string EventType,
        string DisplayName,
        string? Description,
        DateTimeOffset CreatedAt);
}
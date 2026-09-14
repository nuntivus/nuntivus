using System;

namespace KairoServer.Contracts.Events
{
    public sealed record EventDefinitionView(
        string EventType,
        string DisplayName,
        string? Description,
        DateTimeOffset CreatedAt);
}
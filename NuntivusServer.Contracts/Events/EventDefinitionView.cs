using System;

namespace NuntivusServer.Contracts.Events
{
    public sealed record EventDefinitionView(
        string EventType,
        string DisplayName,
        string? Description,
        DateTimeOffset CreatedAt);
}
using KairoServer.Contracts.Events;
using KairoServer.Domain.Events;

namespace KairoServer.Application.Events
{
    internal static class EventDefinitionMapping
    {
        public static EventDefinitionView ToView(this EventDefinition definition) =>
            new(
                definition.EventType,
                definition.DisplayName,
                definition.Description,
                definition.CreatedAt);

        public static EventDefinitionCreated ToCreated(this EventDefinition definition) =>
            new(
                definition.EventType,
                definition.DisplayName,
                definition.Description,
                definition.CreatedAt);
    }
}
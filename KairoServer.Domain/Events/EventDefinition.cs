using System;

namespace KairoServer.Domain.Events
{
    public sealed class EventDefinition
    {
        public string EventType { get; }
        public string DisplayName { get; }
        public string? Description { get; }
        public DateTimeOffset CreatedAt { get; }

        private EventDefinition(
            string eventType,
            string displayName,
            string? description,
            DateTimeOffset createdAt)
        {
            EventType = eventType;
            DisplayName = displayName;
            Description = description;
            CreatedAt = createdAt;
        }

        public static EventDefinition Create(
            string eventType,
            string displayName,
            string? description,
            DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Event type is required.", nameof(eventType));

            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Display name is required.", nameof(displayName));

            return new EventDefinition(
                eventType.Trim(),
                displayName.Trim(),
                string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                now);
        }

        public static EventDefinition Rehydrate(
            string eventType,
            string displayName,
            string? description,
            DateTimeOffset createdAt) =>
            new(eventType, displayName, description, createdAt);
    }
}
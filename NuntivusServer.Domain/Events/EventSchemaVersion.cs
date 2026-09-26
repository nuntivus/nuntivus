using System;

namespace NuntivusServer.Domain.Events
{
    public sealed class EventSchemaVersion
    {
        public string EventType { get; }
        public int Version { get; }
        public string Schema { get; }
        public SchemaCompatibilityMode CompatibilityMode { get; }
        public EventSchemaState State { get; private set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? ActivatedAt { get; private set; }
        public DateTimeOffset? DeprecatedAt { get; private set; }

        private EventSchemaVersion(
            string eventType,
            int version,
            string schema,
            SchemaCompatibilityMode compatibilityMode,
            EventSchemaState state,
            DateTimeOffset createdAt,
            DateTimeOffset? activatedAt,
            DateTimeOffset? deprecatedAt)
        {
            EventType = eventType;
            Version = version;
            Schema = schema;
            CompatibilityMode = compatibilityMode;
            State = state;
            CreatedAt = createdAt;
            ActivatedAt = activatedAt;
            DeprecatedAt = deprecatedAt;
        }

        public static EventSchemaVersion CreateDraft(
            string eventType,
            int version,
            string schema,
            SchemaCompatibilityMode compatibilityMode,
            DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Event type is required.", nameof(eventType));

            if (version < 1)
                throw new ArgumentException("Version must be greater than 0.", nameof(version));

            if (string.IsNullOrWhiteSpace(schema))
                throw new ArgumentException("Schema is required.", nameof(schema));

            return new EventSchemaVersion(
                eventType.Trim(),
                version,
                schema.Trim(),
                compatibilityMode,
                EventSchemaState.Draft,
                now,
                activatedAt: null,
                deprecatedAt: null);
        }

        public static EventSchemaVersion Rehydrate(
            string eventType,
            int version,
            string schema,
            SchemaCompatibilityMode compatibilityMode,
            EventSchemaState state,
            DateTimeOffset createdAt,
            DateTimeOffset? activatedAt,
            DateTimeOffset? deprecatedAt) =>
            new(
                eventType,
                version,
                schema,
                compatibilityMode,
                state,
                createdAt,
                activatedAt,
                deprecatedAt);

        public void Activate(DateTimeOffset now)
        {
            if (State == EventSchemaState.Active)
                return;

            if (State == EventSchemaState.Deprecated)
                throw new InvalidOperationException("Deprecated schema cannot be activated.");

            State = EventSchemaState.Active;
            ActivatedAt = now;
            DeprecatedAt = null;
        }

        public void Deprecate(DateTimeOffset now)
        {
            if (State == EventSchemaState.Deprecated)
                return;

            State = EventSchemaState.Deprecated;
            DeprecatedAt = now;
        }
    }
}
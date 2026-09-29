using System;

namespace NuntivusServer.Domain.Sources
{
    /// <summary>
    /// A <c>Source</c> is the logical producer or integration from which an
    /// <see cref="Events.Event"/> originates (for example an HTTP API caller,
    /// an SDK application, or a Kafka topic bridged by an adapter).
    /// </summary>
    /// <remarks>
    /// Semantics:
    /// <list type="bullet">
    ///   <item><description>Source describes <b>where an event came from</b>; it is intentionally
    ///   kept separate from <see cref="Contracts.Destination"/>, which describes where
    ///   an event is delivered. The two must never be conflated.</description></item>
    ///   <item><description>The canonical <see cref="Events.Event"/> preserves the originating
    ///   source via its <c>SourceId</c> string, which is the stable, normalized value produced by
    ///   <see cref="SourceId"/>. Preservation is a hard invariant of the ingestion pipeline.</description></item>
    /// </list>
    /// </remarks>
    public sealed class Source
    {
        /// <summary>Canonical, normalized identifier of the source.</summary>
        public SourceId Id { get; }

        /// <summary>Human-friendly display name.</summary>
        public string Name { get; private set; }

        /// <summary>Optional free-form description.</summary>
        public string? Description { get; private set; }

        /// <summary>Adapter category this source belongs to.</summary>
        public SourceType Type { get; }

        /// <summary>
        /// Opaque adapter-specific configuration payload (typically JSON) whose schema
        /// is determined by <see cref="Type"/> and validated by the adapter layer.
        /// </summary>
        public string Configuration { get; private set; }

        /// <summary>Whether the source is currently accepting events.</summary>
        public bool Enabled { get; private set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        private Source(
            SourceId id,
            string name,
            string? description,
            SourceType type,
            string configuration,
            bool enabled,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt)
        {
            Id = id;
            Name = name;
            Description = description;
            Type = type;
            Configuration = configuration;
            Enabled = enabled;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        public static Source Create(
            string id,
            string name,
            SourceType type,
            string configuration,
            bool enabled,
            string? description = null,
            DateTimeOffset? now = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Source name cannot be null or whitespace.", nameof(name));

            if (string.IsNullOrWhiteSpace(configuration))
                throw new ArgumentException("Source configuration cannot be null or whitespace.", nameof(configuration));

            var sourceId = SourceId.Create(id);

            var createdAt = now ?? DateTimeOffset.UtcNow;
            return new Source(sourceId, name.Trim(), description, type, configuration, enabled, createdAt, createdAt);
        }

        public static Source Rehydrate(string id, string name, string? description, SourceType type,
            string configuration, bool enabled, DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
            new(SourceId.Create(id), name, description, type, configuration, enabled, createdAt, updatedAt);

        public void Update(string name, string? description, string configuration, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Source name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(configuration))
                throw new ArgumentException("Source configuration is required.", nameof(configuration));

            Name = name.Trim();
            Description = description;
            Configuration = configuration;
            UpdatedAt = now;
        }

        public void Enable(DateTimeOffset now)
        {
            if (Enabled) return;
            Enabled = true;
            UpdatedAt = now;
        }

        public void Disable(DateTimeOffset now)
        {
            if (!Enabled) return;
            Enabled = false;
            UpdatedAt = now;
        }
    }
}

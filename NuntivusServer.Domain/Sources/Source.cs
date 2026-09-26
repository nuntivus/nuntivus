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
    ///   <item><description>At this stage Source is not yet persisted; it is a domain model used
    ///   by adapters to describe producers. Source becomes persisted configuration once
    ///   adapters require per-source secrets/config, quotas, or per-source authorization —
    ///   at which point an <c>ISourceRepository</c> and query support will be introduced.</description></item>
    /// </list>
    /// </remarks>
    public sealed class Source
    {
        /// <summary>Canonical, normalized identifier of the source.</summary>
        public SourceId Id { get; }

        /// <summary>Human-friendly display name.</summary>
        public string Name { get; }

        /// <summary>Optional free-form description.</summary>
        public string? Description { get; }

        /// <summary>Adapter category this source belongs to.</summary>
        public SourceType Type { get; }

        /// <summary>
        /// Opaque adapter-specific configuration payload (typically JSON) whose schema
        /// is determined by <see cref="Type"/> and validated by the adapter layer.
        /// </summary>
        public string Configuration { get; }

        /// <summary>Whether the source is currently accepting events.</summary>
        public bool Enabled { get; }

        private Source(
            SourceId id,
            string name,
            string? description,
            SourceType type,
            string configuration,
            bool enabled)
        {
            Id = id;
            Name = name;
            Description = description;
            Type = type;
            Configuration = configuration;
            Enabled = enabled;
        }

        public static Source Create(
            string id,
            string name,
            SourceType type,
            string configuration,
            bool enabled,
            string? description = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Source name cannot be null or whitespace.", nameof(name));

            if (string.IsNullOrWhiteSpace(configuration))
                throw new ArgumentException("Source configuration cannot be null or whitespace.", nameof(configuration));

            var sourceId = SourceId.Create(id);

            return new Source(sourceId, name.Trim(), description, type, configuration, enabled);
        }
    }
}

using System;
using System.Text.RegularExpressions;

namespace NuntivusServer.Domain.Sources
{
    /// <summary>
    /// Value object representing the stable, canonical identifier of a <see cref="Source"/>.
    /// </summary>
    /// <remarks>
    /// Identity rules:
    /// <list type="bullet">
    ///   <item>Trimmed and lowercased using the invariant culture on construction.</item>
    ///   <item>Must match <c>^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$</c>: URL-safe, log-safe,
    ///   and stable across adapters (HTTP, SDK, Kafka, etc.).</item>
    ///   <item>Two <see cref="SourceId"/> values are equal iff their normalized values are equal.</item>
    /// </list>
    /// The identifier is intentionally opaque to consumers so that adapters (see the future
    /// <c>ISourceAdapter</c> seam) can map their transport-specific producer notion onto a
    /// single canonical <see cref="SourceId"/> preserved on the canonical Event.
    /// </remarks>
    public readonly struct SourceId : IEquatable<SourceId>
    {
        private static readonly Regex Pattern = new(
            "^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public string Value { get; }

        private SourceId(string value)
        {
            Value = value;
        }

        public static SourceId Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Source id cannot be null or whitespace.", nameof(value));

            var normalized = value.Trim().ToLowerInvariant();

            if (!Pattern.IsMatch(normalized))
                throw new ArgumentException(
                    "Source id must be 3-64 chars, start and end with [a-z0-9], and contain only [a-z0-9._-].",
                    nameof(value));

            return new SourceId(normalized);
        }

        public bool Equals(SourceId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is SourceId other && Equals(other);

        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(SourceId left, SourceId right) => left.Equals(right);
        public static bool operator !=(SourceId left, SourceId right) => !left.Equals(right);

        public static implicit operator string(SourceId id) => id.Value;
    }
}

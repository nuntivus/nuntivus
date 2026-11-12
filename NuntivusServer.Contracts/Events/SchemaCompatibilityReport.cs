using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NuntivusServer.Contracts.Events
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SchemaCompatibilityStatus { Compatible, Breaking, Indeterminate }

    public sealed record SchemaCompatibilityFinding(string Path, string Keyword, SchemaCompatibilityStatus Status, string Message);

    public sealed record SchemaCompatibilityReport(
        string EventType,
        int Version,
        int? AgainstVersion,
        SchemaCompatibilityMode Mode,
        SchemaCompatibilityStatus Status,
        IReadOnlyList<SchemaCompatibilityFinding> Findings);

    public sealed record SchemaActivationOptions(bool OverrideCompatibility = false, string? Reason = null);
}

using System.Text.Json;

namespace NuntivusServer.Contracts.Sources
{
    public sealed record SourceView(string Id, string Name, string? Description, string Type,
        bool Enabled, JsonElement Configuration, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}

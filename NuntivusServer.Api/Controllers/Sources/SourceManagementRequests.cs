using System.Text.Json;

namespace NuntivusServer.Api.Controllers.Sources
{
    public sealed record CreateSourceRequest(string Name, string Type, JsonElement Configuration, string? Description = null);
    public sealed record UpdateSourceRequest(string Name, JsonElement Configuration, string? Description = null);
}

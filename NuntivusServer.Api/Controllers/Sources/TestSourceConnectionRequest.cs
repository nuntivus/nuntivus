using System.Text.Json;

namespace NuntivusServer.Api.Controllers.Sources
{
    public sealed record TestSourceConnectionRequest(string Type, JsonElement Configuration);
}

namespace NuntivusServer.Api.Controllers.Events
{
    public sealed record ReplayEventRequest(
        string ReplayKey,
        string? Reason);
}

namespace KairoServer.Api.Controllers.Events
{
    public sealed record ReplayEventRequest(
        string ReplayKey,
        string? Reason);
}

namespace NuntivusServer.Api.Controllers.Events
{
    public sealed record ReplayEventResponse(
        string SourceEventId,
        string ReplayedEventId,
        string Status);
}

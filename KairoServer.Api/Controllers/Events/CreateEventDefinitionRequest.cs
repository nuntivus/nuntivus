namespace KairoServer.Api.Controllers.Events
{
    public sealed record CreateEventDefinitionRequest(
        string EventType,
        string DisplayName,
        string? Description);
}
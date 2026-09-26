namespace NuntivusServer.Api.Controllers.Events
{
    public sealed record CreateEventDefinitionRequest(
        string EventType,
        string DisplayName,
        string? Description);

    public sealed record UpdateEventDefinitionDescriptionRequest(
        string? Description);
}
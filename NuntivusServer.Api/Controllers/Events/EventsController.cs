using NuntivusServer.Application.Events;
using NuntivusServer.Application.Events.Queries;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Common.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace NuntivusServer.Api.Controllers.Events
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/events")]
    public class EventsController : Controller
    {
        private readonly IEventService _events;
        private readonly IEventQueryService _eventQueries;
        private readonly IAppLogger<EventsController> _logger;

        public EventsController(
            IEventService events,
            IEventQueryService eventQueries,
            IAppLogger<EventsController> logger)
        {
            _events = events;
            _eventQueries = eventQueries;
            _logger = logger;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(
            Guid id,
            CancellationToken cancellationToken)
        {
            var result = await _eventQueries.GetByIdAsync(id, cancellationToken);
            return result.ToActionResult(HttpContext, @event => Ok(MapEvent(@event)));
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] EventListApiRequest request,
            CancellationToken cancellationToken)
        {
            var query = new EventListQuery(
                request.Type,
                request.Source,
                request.OccurredFrom,
                request.OccurredTo,
                request.ReceivedFrom,
                request.ReceivedTo,
                request.SortBy,
                request.SortDirection,
                request.Page,
                request.PageSize);

            var result = await _eventQueries.ListAsync(query, cancellationToken);
            return result.ToPagedOk(HttpContext, MapEvent);
        }

        [HttpPost("{id:guid}/replay")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Replay(
            Guid id,
            ReplayEventRequest request,
            CancellationToken cancellationToken)
        {
            _logger.Information(
                "Received ReplayEvent request (eventId {EventId}, replayKey {ReplayKey}).",
                id,
                request.ReplayKey);

            var result = await _events.ReplayAsync(
                id,
                request.ReplayKey,
                request.Reason,
                cancellationToken,
                GetActorId());

            return result.ToActionResult(HttpContext, replay => Ok(new ReplayEventResponse(
                replay.SourceEventId.ToString("D"),
                replay.ReplayedEventId.ToString("D"),
                replay.Created ? "accepted" : "already_replayed")));
        }

        private static EventQueryDto MapEvent(EventQueryView @event)
        {
            return new EventQueryDto(
                @event.Id,
                @event.IdempotencyKey,
                @event.Type,
                @event.Source,
                @event.OccurredAt,
                @event.ReceivedAt,
                ParseJson(@event.Payload),
                ParseOptionalJson(@event.Metadata),
                @event.Deliveries,
                @event.InputSchemaVersion,
                @event.SchemaValidationStatus);
        }

        private static JsonElement ParseJson(string json)
        {
            if (TryParseJsonObject(json, out var element))
                return element;

            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        private static JsonElement? ParseOptionalJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            if (TryParseJsonObject(json, out var element))
                return element;

            return null;
        }

        private static bool TryParseJsonObject(string json, out JsonElement element)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    element = default;
                    return false;
                }

                element = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                element = default;
                return false;
            }
        }

        private string? GetActorId()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return null;

            return User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? User.Identity?.Name;
        }
    }
}

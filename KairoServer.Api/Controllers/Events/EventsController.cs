using KairoServer.Application.Events;
using KairoServer.Api.Common;
using KairoServer.Common.Extensions;
using KairoServer.Common.Logging;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Events
{
    [ApiController]
    [Route("[controller]")]
    public class EventsController : Controller
    {
        private readonly IEventService _events;
        private readonly IAppLogger<EventsController> _logger;
        private readonly IEventPublishRequestValidator _validator;

        public EventsController(
            IEventService events,
            IAppLogger<EventsController> logger,
            IEventPublishRequestValidator validator)
        {
            _events = events;
            _logger = logger;
            _validator = validator;
        }

        [HttpPost]
        public async Task<IActionResult> Publish(
            PublishEventRequest request,
            CancellationToken cancellationToken)
        {
            var validation = _validator.Validate(request);
            if (validation.IsFailure)
                return new BadRequestObjectResult(ApiErrorResponse.From(validation.Errors));

            _logger.Information(
                "Received PublishEvent request (idempotencyKey {IdempotencyKey}, type {Type}, source {Source}).",
                request.IdempotencyKey,
                request.Type,
                request.Source);

            var result = await _events.PublishAsync(
                request.IdempotencyKey,
                request.Type,
                request.Source,
                request.Payload.EnsureJsonObject(),
                request.Timestamp,
                request.Metadata.EnsureJsonObject(),
                cancellationToken);

            var eventId = result.EnsureValue();

            return Ok(new PublishEventResponse(eventId.ToString(), "accepted"));
        }

        [HttpPost("{id:guid}/replay")]
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
                cancellationToken);

            return result.ToActionResult(replay => Ok(new ReplayEventResponse(
                replay.SourceEventId.ToString("D"),
                replay.ReplayedEventId.ToString("D"),
                replay.Created ? "accepted" : "already_replayed")));
        }
    }
}

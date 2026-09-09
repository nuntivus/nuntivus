using KairoServer.Application.Events;
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

        public EventsController(
            IEventService events,
            IAppLogger<EventsController> logger)
        {
            _events = events;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Publish(
            PublishEventRequest request,
            CancellationToken cancellationToken)
        {
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
    }
}

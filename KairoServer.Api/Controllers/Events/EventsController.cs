using System.Text.Json;
using KairoServer.Application.Events.Pub;
using KairoServer.Common.Extensions;
using KairoServer.Common.Logging;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Events
{
    [ApiController]
    [Route("[controller]")]
    public class EventsController : Controller
    {

        private readonly IPublishEventHandler _publishEventHandler;
        private readonly IAppLogger<EventsController> _logger;

        public EventsController(
            IPublishEventHandler publishEventHandler,
            IAppLogger<EventsController> logger)
        {
            _publishEventHandler = publishEventHandler;
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

            var command = new PublishEventCommand(
                request.IdempotencyKey,
                request.Type,
                request.Source,
                request.Payload.EnsureJsonObject(),
                request.Timestamp,
                request.Metadata.EnsureJsonObject());

            var result = await _publishEventHandler.HandleAsync(
                command,
                cancellationToken);

            var valueResult = result.EnsureValue();

            return Ok(new PublishEventResponse(
                    valueResult.ToString(),
                    "accepted"));
        }
    }
}

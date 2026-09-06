using KairoServer.Application.Events.Pub;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Events
{
    public class EventsController : Controller
    {

        private readonly IPublishEventHandler _publishEventHandler;

        public EventsController(IPublishEventHandler publishEventHandler)
        {
            _publishEventHandler = publishEventHandler;
        }

        [HttpPost]
        public async Task<IActionResult> Publish(
        PublishEventRequest request,
        CancellationToken cancellationToken)
        {
            var command = new PublishEventCommand(
                request.IdempotencyKey,
                request.Type,
                request.Source,
                request.Payload.GetRawText(),
                request.Timestamp,
                request.Metadata?.GetRawText() ?? "{}");

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

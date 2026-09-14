using KairoServer.Api.Common;
using KairoServer.Application.Events;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Events;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Events
{
    [ApiController]
    [Route("v1/event-definitions")]
    public sealed class EventDefinitionsController : Controller
    {
        private readonly IEventDefinitionService _definitions;
        private readonly IAppLogger<EventDefinitionsController> _logger;

        public EventDefinitionsController(
            IEventDefinitionService definitions,
            IAppLogger<EventDefinitionsController> logger)
        {
            _definitions = definitions;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] CreateEventDefinitionRequest request,
            CancellationToken cancellationToken)
        {
            _logger.Information("Received CreateEventDefinition request for type {EventType}.", request?.EventType);

            var result = await _definitions.CreateAsync(
                request.EventType,
                request.DisplayName,
                request.Description,
                cancellationToken);

            return result.ToActionResult(created =>
                CreatedAtAction(nameof(GetByEventType), new { eventType = created.EventType }, created));
        }

        [HttpGet("{eventType}")]
        public async Task<IActionResult> GetByEventType(
            string eventType,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.GetByEventTypeAsync(eventType, cancellationToken);
            return result.ToOk();
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] PagedRequest pagination,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.ListAsync(pagination.Page, pagination.PageSize, cancellationToken);

            return result.ToActionResult(paged =>
            {
                var body = new PagedResponse<EventDefinitionView>(
                    paged.Items,
                    paged.Page,
                    paged.PageSize,
                    paged.TotalCount,
                    paged.TotalPages,
                    paged.HasPrevious,
                    paged.HasNext);
                return Ok(body);
            });
        }
    }
}
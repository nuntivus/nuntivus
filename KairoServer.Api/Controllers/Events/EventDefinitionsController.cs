using KairoServer.Api.Common;
using KairoServer.Api.Security;
using KairoServer.Application.Events;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Events
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
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
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
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

            return result.ToActionResult(HttpContext, created =>
                CreatedAtAction(nameof(GetByEventType), new { eventType = created.EventType }, created));
        }

        [HttpGet("{eventType}")]
        public async Task<IActionResult> GetByEventType(
            string eventType,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.GetByEventTypeAsync(eventType, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] PagedRequest pagination,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.ListAsync(pagination.Page, pagination.PageSize, cancellationToken);
            return result.ToPagedOk(HttpContext);
        }

        [HttpPost("{eventType}/schemas")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> CreateSchemaDraft(
            string eventType,
            [FromBody] CreateEventSchemaVersionRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.CreateSchemaDraftAsync(
                eventType,
                request.Version,
                request.Schema,
                request.CompatibilityMode,
                cancellationToken);

            return result.ToActionResult(HttpContext, created =>
                CreatedAtAction(nameof(GetSchemaVersion), new { eventType, version = created.Version }, created));
        }

        [HttpGet("{eventType}/schemas")]
        public async Task<IActionResult> ListSchemaVersions(
            string eventType,
            [FromQuery] PagedRequest pagination,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.ListSchemaVersionsAsync(
                eventType,
                pagination.Page,
                pagination.PageSize,
                cancellationToken);
            return result.ToPagedOk(HttpContext);
        }

        [HttpGet("{eventType}/schemas/{version:int}")]
        public async Task<IActionResult> GetSchemaVersion(
            string eventType,
            int version,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.GetSchemaVersionAsync(eventType, version, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPost("{eventType}/schemas/{version:int}/activate")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> ActivateSchemaVersion(
            string eventType,
            int version,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.ActivateSchemaVersionAsync(eventType, version, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPost("{eventType}/schemas/{version:int}/deprecate")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> DeprecateSchemaVersion(
            string eventType,
            int version,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.DeprecateSchemaVersionAsync(eventType, version, cancellationToken);
            return result.ToOk(HttpContext);
        }
    }
}
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Events;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Security.Claims;

namespace NuntivusServer.Api.Controllers.Events
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

        [HttpPut("{eventType}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Update(
            string eventType,
            [FromBody] UpdateEventDefinitionRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _definitions.UpdateAsync(
                eventType,
                request.DisplayName,
                request?.Description,
                cancellationToken);

            return result.ToOk(HttpContext);
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
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SchemaActivationOptions? request,
            CancellationToken cancellationToken)
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            var result = await _definitions.ActivateSchemaVersionAsync(eventType, version, cancellationToken,
                actorId, User.IsInRole("Admin"), request);
            return result.ToOk(HttpContext);
        }

        [HttpGet("{eventType}/schemas/{version:int}/compatibility")]
        public async Task<IActionResult> CompareSchemaVersions(string eventType, int version,
            [FromQuery] int? againstVersion, CancellationToken cancellationToken)
        {
            var result = await _definitions.CompareSchemaVersionsAsync(eventType, version, againstVersion, cancellationToken);
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
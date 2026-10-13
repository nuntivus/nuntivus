using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Api.Controllers.Events;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common.Extensions;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.Sources;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Api.Controllers.Sources
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/sources")]
    public sealed class SourcesController : ControllerBase
    {
        private readonly ISourceService _sources;
        private readonly ISourceIngestionService _ingestion;
        private readonly IHttpSourceAuthenticator _authenticator;
        private readonly IWebhookIngestRequestValidator _validator;
        private readonly IAppLogger<SourcesController> _logger;
        private readonly ISourceConfigurationRegistry _configurations;
        private readonly IReadOnlyDictionary<SourceType, ISourceConnectionTester> _testers;

        public SourcesController(ISourceService sources, ISourceIngestionService ingestion,
            IHttpSourceAuthenticator authenticator, IWebhookIngestRequestValidator validator,
            IAppLogger<SourcesController> logger, ISourceConfigurationRegistry configurations,
            IEnumerable<ISourceConnectionTester> testers)
        {
            _sources = sources;
            _ingestion = ingestion;
            _authenticator = authenticator;
            _validator = validator;
            _logger = logger;
            _configurations = configurations;
            _testers = testers.ToDictionary(tester => tester.Type);
        }

        [HttpPost("test-connection")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public Task<IActionResult> TestNewConnection([FromBody] TestSourceConnectionRequest request, CancellationToken cancellationToken) =>
            TestConnection(request, null, cancellationToken);

        [HttpPost("{id:guid}/test-connection")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public Task<IActionResult> TestExistingConnection(Guid id, [FromBody] TestSourceConnectionRequest request, CancellationToken cancellationToken) =>
            TestConnection(request, id, cancellationToken);

        private async Task<IActionResult> TestConnection(TestSourceConnectionRequest request, Guid? id, CancellationToken cancellationToken)
        {
            SourceType type;
            if (id is { } sourceId)
            {
                var existing = await _sources.GetByIdAsync(sourceId.ToString("D"), cancellationToken);
                if (existing.IsFailure) return ErrorResult(existing.Errors);
                type = existing.Value.Type;
            }
            else if (!Enum.TryParse(request.Type, true, out type) || !Enum.IsDefined(type))
                return ErrorResult([NuntivusServer.Common.Error.Validation("Unknown Source type.")]);

            var config = _configurations.Normalize(type, ConfigurationText(request.Configuration));
            if (config.IsFailure) return ErrorResult(config.Errors);
            if (!_testers.TryGetValue(type, out var tester))
                return ErrorResult([NuntivusServer.Common.Error.Validation("Connection test is not supported for this Source type.")]);
            var result = await tester.TestAsync(config.Value, cancellationToken);
            return result.ToActionResult(HttpContext, message => Ok(new { message }));
        }

        [HttpPost]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Create([FromBody] CreateSourceRequest request, CancellationToken cancellationToken)
        {
            var result = await _sources.CreateAsync(request.Name, request.Type, ConfigurationText(request.Configuration),
                request.Description, cancellationToken, ActorId());
            return result.ToActionResult(HttpContext, source =>
                CreatedAtAction(nameof(GetById), new { id = source.Id.Value }, Map(source)));
        }

        [AcceptVerbs("POST", "PUT", "PATCH", Route = "{id:guid}/events")]
        [AllowAnonymous]
        public async Task<IActionResult> Ingest(Guid id, [FromBody] WebhookIngestRequest request,
            CancellationToken cancellationToken)
        {
            var sourceResult = await _sources.GetByIdAsync(id.ToString("D"), cancellationToken);
            if (sourceResult.IsFailure)
                return ErrorResult(sourceResult.Errors);
            var source = sourceResult.Value;
            if (!source.Enabled)
                return ErrorResult([NuntivusServer.Common.Error.Conflict("Source is disabled.")]);
            if (source.Type != SourceType.HTTP_API)
                return ErrorResult([NuntivusServer.Common.Error.Validation("Source is not an HTTP Source.")]);

            using var sourceConfiguration = JsonDocument.Parse(source.Configuration);
            var method = sourceConfiguration.RootElement.TryGetProperty("method", out var selectedMethod)
                ? selectedMethod.GetString()! : "POST";
            if (!string.Equals(Request.Method, method, StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers.Allow = method;
                return StatusCode(StatusCodes.Status405MethodNotAllowed);
            }

            var authentication = await _authenticator.AuthenticateAsync(source,
                Request.Headers["X-Nuntivus-Source-Token"].ToString(), cancellationToken);
            if (authentication.IsFailure)
                return ErrorResult(authentication.Errors);

            var validation = _validator.Validate(request);
            if (validation.IsFailure)
                return ErrorResult(validation.Errors);

            var result = await _ingestion.IngestAsync(SourceType.HTTP_API, source.Id.Value,
                new HttpApiSourceEventContext(request.IdempotencyKey ?? Request.Headers["X-Nuntivus-Idempotency-Key"].ToString(),
                    request.Type ?? Request.Headers["X-Nuntivus-Event-Type"].ToString(), source.Id.Value,
                    request.Timestamp ?? (DateTimeOffset.TryParse(Request.Headers["X-Nuntivus-Occurred-At"], out var occurredAt)
                        ? occurredAt : DateTimeOffset.UtcNow),
                    request.Payload.EnsureJsonObject(), request.Metadata.EnsureJsonObject()), cancellationToken);
            if (result.IsSuccess)
                _logger.Information("Source event accepted (SourceId {SourceId}, SourceType {SourceType}, EventId {EventId}).",
                    source.Id.Value, source.Type, result.Value.EventId);
            return result.ToActionResult(HttpContext, ack => Ok(new PublishEventResponse(ack.EventId, ack.Status)));
        }

        private IActionResult ErrorResult(System.Collections.Generic.IReadOnlyList<NuntivusServer.Common.Error> errors)
        {
            var response = ApiErrorResponse.From(errors, HttpContext);
            return new ObjectResult(response) { StatusCode = response.Status };
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _sources.GetByIdAsync(id.ToString("D"), cancellationToken);
            return result.ToActionResult(HttpContext, source => Ok(Map(source)));
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] PagedRequest request, CancellationToken cancellationToken)
        {
            var result = await _sources.ListAsync(request.Page, request.PageSize, cancellationToken);
            return result.ToPagedOk(HttpContext, Map);
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSourceRequest request, CancellationToken cancellationToken)
        {
            var result = await _sources.UpdateAsync(id.ToString("D"), request.Name, ConfigurationText(request.Configuration),
                request.Description, cancellationToken, ActorId());
            return result.ToActionResult(HttpContext, source => Ok(Map(source)));
        }

        [HttpPost("{id:guid}/enable")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Enable(Guid id, CancellationToken cancellationToken)
        {
            var result = await _sources.EnableAsync(id.ToString("D"), cancellationToken, ActorId());
            return result.ToActionResult(HttpContext, source => Ok(Map(source)));
        }

        [HttpPost("{id:guid}/disable")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken)
        {
            var result = await _sources.DisableAsync(id.ToString("D"), cancellationToken, ActorId());
            return result.ToActionResult(HttpContext, source => Ok(Map(source)));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            var result = await _sources.DeleteAsync(id.ToString("D"), cancellationToken, ActorId());
            return result.IsSuccess ? NoContent() : new ObjectResult(ApiErrorResponse.From(result.Errors, HttpContext))
            {
                StatusCode = ApiErrorResponse.MapStatusCode(result.Error?.Code)
            };
        }

        private string? ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name;

        private static string ConfigurationText(JsonElement configuration) =>
            configuration.ValueKind == JsonValueKind.Undefined ? "{}" : configuration.GetRawText();

        private static SourceView Map(Source source)
        {
            using var document = JsonDocument.Parse(source.Configuration);
            return new SourceView(source.Id.Value, source.Name, source.Description, source.Type.ToString(),
                source.Enabled, document.RootElement.Clone(), source.CreatedAt, source.UpdatedAt);
        }
    }
}

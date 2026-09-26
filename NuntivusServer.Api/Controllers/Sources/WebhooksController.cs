using System;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Controllers.Events;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Common.Extensions;
using NuntivusServer.Common.Logging;
using NuntivusServer.Domain.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NuntivusServer.Api.Controllers.Sources
{
    [ApiController]
    [Route("v1/sources/webhooks")]
    public sealed class WebhooksController : Controller
    {
        private const string IdempotencyHeader = "X-Nuntivus-Idempotency-Key";
        private const string EventTypeHeader = "X-Nuntivus-Event-Type";
        private const string TimestampHeader = "X-Nuntivus-Occurred-At";

        private readonly ISourceIngestionService _sourceIngestion;
        private readonly IWebhookIngestRequestValidator _validator;
        private readonly IAppLogger<WebhooksController> _logger;

        public WebhooksController(
            ISourceIngestionService sourceIngestion,
            IWebhookIngestRequestValidator validator,
            IAppLogger<WebhooksController> logger)
        {
            _sourceIngestion = sourceIngestion;
            _validator = validator;
            _logger = logger;
        }

        [HttpPost("{source}")]
        [AllowAnonymous]
        public async Task<IActionResult> Ingest(
            string source,
            [FromBody] WebhookIngestRequest request,
            CancellationToken cancellationToken)
        {
            var validation = _validator.Validate(request);
            if (validation.IsFailure)
                return new BadRequestObjectResult(ApiErrorResponse.From(validation.Errors, HttpContext));

            var idempotencyKey = request.IdempotencyKey ?? Request.Headers[IdempotencyHeader].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return BadRequestValidation("idempotencyKey is required.");

            var type = request.Type ?? Request.Headers[EventTypeHeader].ToString();
            if (string.IsNullOrWhiteSpace(type))
                return BadRequestValidation("type is required.");

            var timestamp = request.Timestamp;
            if (timestamp is null
                && DateTimeOffset.TryParse(Request.Headers[TimestampHeader].ToString(), out var headerTimestamp))
            {
                timestamp = headerTimestamp;
            }

            _logger.Information(
                "Received generic webhook ingest request (source {Source}, idempotencyKey {IdempotencyKey}, type {Type}).",
                source,
                idempotencyKey,
                type);

            var result = await _sourceIngestion.IngestAsync(
                SourceType.GENERIC_WEBHOOK,
                new GenericWebhookSourceEventContext(
                    source,
                    idempotencyKey,
                    type,
                    timestamp ?? DateTimeOffset.UtcNow,
                    request.Payload.EnsureJsonObject(),
                    request.Metadata.EnsureJsonObject()),
                cancellationToken);

            return result.ToActionResult(HttpContext, ack =>
                Ok(new PublishEventResponse(ack.EventId, ack.Status)));
        }

        private IActionResult BadRequestValidation(string message)
        {
            return new BadRequestObjectResult(ApiErrorResponse.From(
                new[] { Error.Validation(message) },
                HttpContext));
        }
    }
}

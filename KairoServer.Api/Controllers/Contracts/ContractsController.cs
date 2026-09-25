using System;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Api.Controllers.Events;
using KairoServer.Api.Security;
using KairoServer.Application.Contracts;
using KairoServer.Application.Matching;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace KairoServer.Api.Controllers.Contracts
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/[controller]")]
    public class ContractsController : Controller
    {
        private readonly IContractService _contracts;
        private readonly IContractTestEventService _testEvents;
        private readonly IEventPublishRequestValidator _eventValidator;
        private readonly IAppLogger<ContractsController> _logger;

        public ContractsController(
            IContractService contracts,
            IContractTestEventService testEvents,
            IEventPublishRequestValidator eventValidator,
            IAppLogger<ContractsController> logger)
        {
            _contracts = contracts;
            _testEvents = testEvents;
            _eventValidator = eventValidator;
            _logger = logger;
        }

        [HttpPost("{id:guid}/test/preview")]
        public async Task<IActionResult> PreviewTestEvent(
            Guid id,
            [FromBody] ContractTestEventRequest request,
            CancellationToken cancellationToken)
        {
            var validation = ValidateTestEvent(id, request);
            if (validation.IsFailure)
                return new BadRequestObjectResult(ApiErrorResponse.From(validation.Errors, HttpContext));

            return (await _testEvents.PreviewAsync(id, request, cancellationToken)).ToOk(HttpContext);
        }

        [HttpPost("{id:guid}/test/send")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> SendTestEvent(
            Guid id,
            [FromBody] ContractTestEventRequest request,
            CancellationToken cancellationToken)
        {
            var validation = ValidateTestEvent(id, request);
            if (validation.IsFailure)
                return new BadRequestObjectResult(ApiErrorResponse.From(validation.Errors, HttpContext));

            return (await _testEvents.SendAsync(id, request, GetActorId() ?? string.Empty, cancellationToken))
                .ToOk(HttpContext);
        }

        private Result ValidateTestEvent(Guid id, ContractTestEventRequest request)
        {
            if (request is null || request.Payload.ValueKind != JsonValueKind.Object)
                return Result.Failure(Error.Validation("payload must be a JSON object."));

            return _eventValidator.Validate(new PublishEventRequest(
                "test", request.Type, id.ToString("D"), DateTimeOffset.UtcNow,
                request.Metadata, request.Payload));
        }

        [HttpPost]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Create(
            [FromBody] CreateContractRequest request,
            CancellationToken cancellationToken)
        {
            _logger.Information(
                "Received CreateContract request for customer {CustomerId}.",
                request?.CustomerId);

            var result = await _contracts.CreateAsync(request!, cancellationToken, GetActorId());

            return result.ToActionResult(HttpContext, created =>
                CreatedAtAction(nameof(GetById), new { id = created.Id }, created));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _contracts.GetByIdAsync(id, cancellationToken);
            return result.ToActionResult(HttpContext, contract =>
            {
                Response.Headers.ETag = BuildEtag(contract.UpdatedAt);
                return Ok(contract);
            });
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        [SwaggerOperation(
            Summary = "Replace contract",
            Description = "Replaces contract configuration with optimistic concurrency via If-Match ETag. Example header: If-Match: \"8e9f...\".")]
        public async Task<IActionResult> Replace(
            Guid id,
            [FromBody] UpdateContractRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryParseIfMatch(Request.Headers.IfMatch.ToString(), out var expectedUpdatedAt))
            {
                return new BadRequestObjectResult(ApiErrorResponse.From(new[]
                {
                    KairoServer.Common.Error.Validation("If-Match header with a valid contract ETag is required.")
                }, HttpContext));
            }

            var result = await _contracts.ReplaceAsync(id, request, expectedUpdatedAt, cancellationToken, GetActorId());
            return result.ToActionResult(HttpContext, contract =>
            {
                Response.Headers.ETag = BuildEtag(contract.UpdatedAt);
                return Ok(contract);
            });
        }

        [HttpPost("{id:guid}/enable")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Enable(Guid id, CancellationToken cancellationToken)
        {
            var result = await _contracts.EnableAsync(id, cancellationToken, GetActorId());
            return result.ToOk(HttpContext);
        }

        [HttpPost("{id:guid}/disable")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken)
        {
            var result = await _contracts.DisableAsync(id, cancellationToken, GetActorId());
            return result.ToOk(HttpContext);
        }

        [HttpPost("{id:guid}/credentials")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> UpdateCredentials(
            Guid id,
            [FromBody] UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _contracts.UpdateCredentialsAsync(id, request, cancellationToken, GetActorId());
            return result.ToOk(HttpContext);
        }

        [HttpPost("{id:guid}/credentials/rotate")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> RotateCredentials(
            Guid id,
            [FromBody] UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _contracts.RotateCredentialsAsync(id, request, cancellationToken, GetActorId());
            return result.ToOk(HttpContext);
        }

        private string? GetActorId()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return null;

            return User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? User.Identity?.Name;
        }

        private static string BuildEtag(DateTimeOffset updatedAt) =>
            $"\"{updatedAt.UtcTicks.ToString("x", CultureInfo.InvariantCulture)}\"";

        private static bool TryParseIfMatch(string? ifMatch, out DateTimeOffset expectedUpdatedAt)
        {
            expectedUpdatedAt = default;
            if (string.IsNullOrWhiteSpace(ifMatch))
                return false;

            var token = ifMatch.Trim();
            if (token.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                token = token[2..].Trim();

            token = token.Trim('"');
            if (!long.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ticks))
                return false;

            if (ticks <= 0)
                return false;

            expectedUpdatedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
    }
}

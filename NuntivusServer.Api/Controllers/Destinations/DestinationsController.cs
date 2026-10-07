using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Common.Extensions;
using NuntivusServer.Contracts.Destinations;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Api.Controllers.Destinations
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/destinations")]
    public sealed class DestinationsController : ControllerBase
    {
        private readonly IDestinationService _destinations;
        private readonly IDestinationConfigDispatcher _configs;

        public DestinationsController(IDestinationService destinations, IDestinationConfigDispatcher configs)
        {
            _destinations = destinations;
            _configs = configs;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] PagedRequest request, CancellationToken cancellationToken)
        {
            var result = await _destinations.ListAsync(request.Page, request.PageSize, cancellationToken);
            return result.ToPagedOk(HttpContext, Map);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _destinations.GetByIdAsync(id, cancellationToken);
            return result.ToActionResult(HttpContext, item => Ok(Map(item)));
        }

        [HttpPost]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Create([FromBody] CreateDestinationRequest request, CancellationToken cancellationToken)
        {
            var result = request.Configuration is not null
                ? await _destinations.CreateAsync(request.Name, request.Description, request.Configuration,
                    cancellationToken, ActorId())
                : request.Type is { } type
                    ? await _destinations.CreateDraftAsync(request.Name, request.Description,
                        (NuntivusServer.Domain.Contracts.DestinationType)type, cancellationToken, ActorId())
                    : NuntivusServer.Common.Result.Failure<DestinationResource>(
                        NuntivusServer.Common.Error.Validation("Destination type is required."));
            return result.ToActionResult(HttpContext, item => CreatedAtAction(nameof(GetById), new { item.Id }, Map(item)));
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDestinationRequest request, CancellationToken cancellationToken)
        {
            var result = await _destinations.UpdateAsync(id, request.Name, request.Description, request.Configuration,
                cancellationToken, ActorId());
            return result.ToActionResult(HttpContext, item => Ok(Map(item)));
        }

        [HttpPost("{id:guid}/disable")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Disable(Guid id, [FromBody] DisableDestinationRequest request, CancellationToken cancellationToken)
        {
            var result = await _destinations.SetEnabledAsync(id, false, request.PauseExistingBindings, cancellationToken, ActorId());
            return result.ToActionResult(HttpContext, item => Ok(Map(item)));
        }

        [HttpPost("{id:guid}/enable")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Enable(Guid id, CancellationToken cancellationToken)
        {
            var result = await _destinations.SetEnabledAsync(id, true, false, cancellationToken, ActorId());
            return result.ToActionResult(HttpContext, item => Ok(Map(item)));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Delete(Guid id, [FromQuery] bool removeBindings, CancellationToken cancellationToken)
        {
            var result = await _destinations.DeleteAsync(id, removeBindings, cancellationToken, ActorId());
            return result.IsSuccess ? NoContent() : new ObjectResult(ApiErrorResponse.From(result.Errors, HttpContext))
            {
                StatusCode = ApiErrorResponse.MapStatusCode(result.Error?.Code)
            };
        }

        private DestinationResourceView Map(DestinationResource item) =>
            new(item.Id, item.Name, item.Description,
                Enum.Parse<NuntivusServer.Contracts.CustomerContracts.DestinationType>(item.Destination.Type.ToString()),
                item.Configured ? _configs.Redact(item.Destination).EnsureValue() : "{}", item.Enabled, item.DisablePolicy,
                item.CreatedAt, item.UpdatedAt, item.Configured);

        private string? ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name;
    }
}

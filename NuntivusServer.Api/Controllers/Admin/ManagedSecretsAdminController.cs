using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Api.Controllers.Admin
{
    public sealed record CreateManagedSecretRequest(string Name, string Value);
    public sealed record UpdateManagedSecretRequest(string Value);

    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
    [Route("v1/admin/secrets")]
    public sealed class ManagedSecretsAdminController : ControllerBase
    {
        private readonly IManagedSecretRepository _secrets;
        private readonly IDestinationCredentialProtector _protector;

        public ManagedSecretsAdminController(IManagedSecretRepository secrets, IDestinationCredentialProtector protector)
        {
            _secrets = secrets;
            _protector = protector;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken) =>
            Ok(await _secrets.ListAsync(cancellationToken));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateManagedSecretRequest request, CancellationToken cancellationToken)
        {
            var name = request?.Name?.Trim();
            if (name is null || !IsValidName(name))
                return BadRequest(ApiErrorResponse.From(
                    new[] { Error.Validation("Name must be a valid secret reference (e.g. secret.acme.webhook).") }, HttpContext));

            if (string.IsNullOrWhiteSpace(request!.Value) || request.Value.Length > 4096)
                return BadRequest(ApiErrorResponse.From(
                    new[] { Error.Validation("Value must contain between 1 and 4096 characters.") }, HttpContext));

            var protectedValue = _protector.Protect(request.Value);
            if (protectedValue.IsFailure)
                return BadRequest(ApiErrorResponse.From(protectedValue.Errors, HttpContext));

            if (!await _secrets.CreateAsync(name, protectedValue.Value, cancellationToken))
                return Conflict(new { message = "A secret with this name already exists." });

            return StatusCode(201, new { name, currentVersion = 1 });
        }

        [HttpGet("{name}/versions")]
        public async Task<IActionResult> ListVersions(string name, CancellationToken cancellationToken)
        {
            if (!IsValidName(name))
                return BadRequest();

            var versions = await _secrets.ListVersionsAsync(name, cancellationToken);
            return versions is null ? NotFound() : Ok(versions);
        }

        [HttpPut("{name}")]
        public async Task<IActionResult> Update(string name, [FromBody] UpdateManagedSecretRequest request, CancellationToken cancellationToken)
        {
            if (!IsValidName(name))
                return BadRequest();

            if (string.IsNullOrWhiteSpace(request?.Value) || request.Value.Length > 4096)
                return BadRequest(ApiErrorResponse.From(
                    new[] { Error.Validation("Value must contain between 1 and 4096 characters.") }, HttpContext));

            var protectedValue = _protector.Protect(request.Value);
            if (protectedValue.IsFailure)
                return BadRequest(ApiErrorResponse.From(protectedValue.Errors, HttpContext));

            var version = await _secrets.AddVersionAsync(name, protectedValue.Value, cancellationToken);
            return version is null ? NotFound() : Ok(new { name, currentVersion = version });
        }

        [HttpDelete("{name}")]
        public async Task<IActionResult> Delete(string name, CancellationToken cancellationToken)
        {
            if (!IsValidName(name))
                return BadRequest();

            return await _secrets.DeleteAsync(name, cancellationToken) switch
            {
                ManagedSecretDeleteResult.Deleted => NoContent(),
                ManagedSecretDeleteResult.Referenced => Conflict(new { message = "This secret is referenced by a contract or an unfinished delivery." }),
                _ => NotFound(),
            };
        }

        private static bool IsValidName(string? name) =>
            ConfigReference.TryParse(name, "secret", out var reference) && name == reference.ToString();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Api.Common;
using NuntivusServer.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Security;
using NuntivusServer.Contracts.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NuntivusServer.Api.Controllers.Admin
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/admin/users")]
    public sealed class UsersAdminController : ControllerBase
    {
        private readonly IAdminUserRoleService _adminUsers;
        private readonly IAdminLocalCredentialService _credentials;

        public UsersAdminController(
            IAdminUserRoleService adminUsers,
            IAdminLocalCredentialService credentials)
        {
            _adminUsers = adminUsers;
            _credentials = credentials;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            var result = await _adminUsers.ListAsync(cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet("{subject}")]
        public async Task<IActionResult> GetBySubject(string subject, CancellationToken cancellationToken)
        {
            var result = await _adminUsers.GetAsync(subject, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet("me")]
        public IActionResult Me()
        {
            var principal = User;
            if (principal?.Identity?.IsAuthenticated != true)
                return Unauthorized();

            var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue("sub")
                ?? principal.FindFirstValue(ClaimTypes.Name)
                ?? principal.Identity?.Name;

            if (string.IsNullOrWhiteSpace(subject))
                return Unauthorized();

            var displayName = principal.FindFirstValue(ClaimTypes.Name)
                ?? principal.Identity?.Name;

            var claimRoles = principal.Claims
                .Where(static claim => claim.Type == ClaimTypes.Role || claim.Type == "role" || claim.Type == "roles")
                .Select(static claim => claim.Value)
                .ToArray();

            var roles = AdminPermissionCatalog.NormalizeRoles(claimRoles);
            var permissions = AdminPermissionCatalog.GetPermissionsForRoles(roles);

            return Ok(new AdminCurrentUserView(subject, displayName, roles, permissions));
        }

        [HttpPut("{subject}/roles")]
        [Authorize(Policy = AdminAuthorizationPolicies.Admin)]
        public async Task<IActionResult> ReplaceRoles(
            string subject,
            [FromBody] ReplaceAdminUserRolesRequest request,
            CancellationToken cancellationToken)
        {
            var roleValidation = ValidateRoles(request.Roles);
            if (roleValidation.IsFailure)
                return roleValidation.ErrorResult!;

            var result = await _adminUsers.ReplaceAsync(
                subject,
                request.DisplayName,
                roleValidation.Roles,
                GetActorId(),
                cancellationToken);

            if (result.IsFailure)
                return result.ToOk(HttpContext);

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                var passwordResult = await _credentials.SetPasswordAsync(
                    subject,
                    request.Password,
                    GetActorId(),
                    cancellationToken);

                if (passwordResult.IsFailure)
                {
                    var payload = ApiErrorResponse.From(passwordResult.Errors, HttpContext);
                    return new ObjectResult(payload)
                    {
                        StatusCode = payload.Status
                    };
                }
            }

            return result.ToOk(HttpContext);
        }

        [HttpPost]
        [Authorize(Policy = AdminAuthorizationPolicies.Admin)]
        public async Task<IActionResult> Create(
            [FromBody] CreateAdminUserRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Subject))
            {
                var payload = ApiErrorResponse.From(
                    [Error.Validation("subject is required.")],
                    HttpContext);
                return new ObjectResult(payload)
                {
                    StatusCode = payload.Status
                };
            }

            var roleValidation = ValidateRoles(request.Roles);
            if (roleValidation.IsFailure)
                return roleValidation.ErrorResult!;

            var result = await _adminUsers.ReplaceAsync(
                request.Subject,
                request.DisplayName,
                roleValidation.Roles,
                GetActorId(),
                cancellationToken);

            if (result.IsFailure)
                return result.ToOk(HttpContext);

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                var passwordResult = await _credentials.SetPasswordAsync(
                    request.Subject,
                    request.Password,
                    GetActorId(),
                    cancellationToken);

                if (passwordResult.IsFailure)
                {
                    var payload = ApiErrorResponse.From(passwordResult.Errors, HttpContext);
                    return new ObjectResult(payload)
                    {
                        StatusCode = payload.Status
                    };
                }
            }

            return result.ToOk(HttpContext);
        }

        [HttpDelete("{subject}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Admin)]
        public async Task<IActionResult> Delete(
            string subject,
            CancellationToken cancellationToken)
        {
            var result = await _adminUsers.DeleteAsync(subject, GetActorId(), cancellationToken);
            if (result.IsSuccess)
                return NoContent();

            var payload = ApiErrorResponse.From(result.Errors, HttpContext);
            return new ObjectResult(payload)
            {
                StatusCode = payload.Status
            };
        }

        private string? GetActorId()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return null;

            return User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? User.Identity?.Name;
        }

        private (bool IsFailure, IActionResult? ErrorResult, IReadOnlyList<string> Roles) ValidateRoles(IReadOnlyList<string>? roles)
        {
            var inputRoles = roles ?? [];
            var uniqueInputRoles = inputRoles
                .Where(static role => !string.IsNullOrWhiteSpace(role))
                .Select(static role => role.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var normalizedRoles = AdminPermissionCatalog.NormalizeRoles(uniqueInputRoles);
            if (normalizedRoles.Count == uniqueInputRoles.Length)
                return (false, null, normalizedRoles);

            var unknownRoles = uniqueInputRoles
                .Except(normalizedRoles, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var payload = ApiErrorResponse.From(
                [Error.Validation($"Unknown roles: {string.Join(", ", unknownRoles)}")],
                HttpContext);

            return (true, new ObjectResult(payload)
            {
                StatusCode = payload.Status
            }, Array.Empty<string>());
        }
    }
}
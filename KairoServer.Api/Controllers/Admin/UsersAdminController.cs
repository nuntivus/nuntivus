using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Common;
using KairoServer.Api.Security;
using KairoServer.Application.Security;
using KairoServer.Contracts.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Admin
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/admin/users")]
    public sealed class UsersAdminController : ControllerBase
    {
        private readonly IAdminUserRoleService _adminUsers;

        public UsersAdminController(IAdminUserRoleService adminUsers)
        {
            _adminUsers = adminUsers;
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
            var inputRoles = request.Roles ?? [];
            var uniqueInputRoles = inputRoles
                .Where(static role => !string.IsNullOrWhiteSpace(role))
                .Select(static role => role.Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(static role => role, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var normalizedRoles = AdminPermissionCatalog.NormalizeRoles(uniqueInputRoles);

            if (normalizedRoles.Count != uniqueInputRoles.Length)
            {
                var unknownRoles = uniqueInputRoles
                    .Except(normalizedRoles, System.StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var payload = ApiErrorResponse.From(
                    [Error.Validation($"Unknown roles: {string.Join(", ", unknownRoles)}")],
                    HttpContext);
                return new ObjectResult(payload)
                {
                    StatusCode = payload.Status
                };
            }

            var result = await _adminUsers.ReplaceAsync(
                subject,
                request.DisplayName,
                normalizedRoles,
                GetActorId(),
                cancellationToken);

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
    }
}
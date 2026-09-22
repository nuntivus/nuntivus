using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using KairoServer.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Auth
{
    [ApiController]
    [Route("v1/auth")]
    public sealed class AuthController : ControllerBase
    {
        [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var principal = User;
            if (principal?.Identity?.IsAuthenticated != true)
                return Unauthorized();

            var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue(TokenClaimTypes.Subject)
                ?? principal.FindFirstValue(ClaimTypes.Name)
                ?? principal.Identity?.Name
                ?? string.Empty;

            var displayName = principal.FindFirstValue(ClaimTypes.Name)
                ?? principal.Identity?.Name;

            var roles = principal.Claims
                .Where(static claim => claim.Type == ClaimTypes.Role || claim.Type == TokenClaimTypes.Role || claim.Type == TokenClaimTypes.Roles)
                .Select(static claim => claim.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Ok(new CurrentUserResponse(subject, displayName, roles, IsAuthenticated: true));
        }

        public sealed record CurrentUserResponse(
            string Subject,
            string? DisplayName,
            IReadOnlyList<string> Roles,
            bool IsAuthenticated);
    }
}
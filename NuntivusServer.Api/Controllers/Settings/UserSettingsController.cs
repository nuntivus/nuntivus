using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Settings;
using NuntivusServer.Contracts.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace NuntivusServer.Api.Controllers.Settings
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/settings/me")]
    public sealed class UserSettingsController : ControllerBase
    {
        private readonly IUserSettingsService _userSettings;

        public UserSettingsController(IUserSettingsService userSettings)
        {
            _userSettings = userSettings;
        }

        [HttpGet("preferences")]
        [SwaggerOperation(
            Summary = "Get current user preferences",
            Description = "Returns preferences for the authenticated subject only.")]
        public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
        {
            var subject = GetSubject();
            if (subject is null)
                return Unauthorized();

            var result = await _userSettings.GetPreferencesAsync(subject, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPut("preferences")]
        [SwaggerOperation(
            Summary = "Update current user preferences",
            Description = "Updates preferences for the authenticated subject only. Example request: { \"theme\": \"dark\", \"defaultPageSize\": 50, \"denseTables\": true }.")]
        public async Task<IActionResult> UpdatePreferences(
            [FromBody] UpdateUserPreferencesRequest request,
            CancellationToken cancellationToken)
        {
            var subject = GetSubject();
            if (subject is null)
                return Unauthorized();

            var result = await _userSettings.UpdatePreferencesAsync(subject, request, cancellationToken);
            return result.ToOk(HttpContext);
        }

        private string? GetSubject()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return null;

            var subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? User.Identity?.Name;

            return string.IsNullOrWhiteSpace(subject) ? null : subject;
        }
    }
}
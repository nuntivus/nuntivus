using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Api.Security;
using KairoServer.Application.Settings;
using KairoServer.Contracts.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Settings
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
        public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
        {
            var subject = GetSubject();
            if (subject is null)
                return Unauthorized();

            var result = await _userSettings.GetPreferencesAsync(subject, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPut("preferences")]
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
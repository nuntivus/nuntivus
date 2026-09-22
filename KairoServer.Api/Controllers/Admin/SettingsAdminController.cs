using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Api.Security;
using KairoServer.Application.Settings;
using KairoServer.Contracts.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Admin
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Admin)]
    [Route("v1/admin/settings")]
    public sealed class SettingsAdminController : ControllerBase
    {
        private readonly IInstanceSettingsService _instanceSettings;

        public SettingsAdminController(IInstanceSettingsService instanceSettings)
        {
            _instanceSettings = instanceSettings;
        }

        [HttpGet("instance")]
        public async Task<IActionResult> GetInstance(CancellationToken cancellationToken)
        {
            var result = await _instanceSettings.GetAsync(cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPut("instance")]
        public async Task<IActionResult> UpdateInstance(
            [FromBody] UpdateInstanceSettingsRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _instanceSettings.UpdateAsync(request, GetActorId(), cancellationToken);
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
    }
}
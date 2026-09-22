using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Api.Security;
using KairoServer.Application.Settings;
using KairoServer.Contracts.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

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
        [SwaggerOperation(
            Summary = "Get instance settings",
            Description = "Returns current instance-level defaults and limits for delivery, retry, retention, security, and payload constraints.")]
        public async Task<IActionResult> GetInstance(CancellationToken cancellationToken)
        {
            var result = await _instanceSettings.GetAsync(cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPut("instance")]
        [SwaggerOperation(
            Summary = "Update instance settings",
            Description = "Updates instance-level settings. Example request: { \"deliveryDefaults\": { \"requestTimeoutSeconds\": 30, \"maxAttempts\": 6, \"batchLimit\": 500 }, \"retryDefaults\": { \"initialDelaySeconds\": 30, \"maxDelaySeconds\": 3600, \"backoffMultiplier\": 2.0 }, \"security\": { \"requireHttpsDestinations\": true, \"allowPrivateNetworkDestinations\": false, \"requireDestinationCredentialEncryption\": true }, \"retention\": { \"eventRetentionDays\": 90, \"deliveryAuditRetentionDays\": 365, \"operationAuditRetentionDays\": 365 }, \"limits\": { \"maxEventPayloadBytes\": 262144, \"maxEventMetadataBytes\": 65536, \"maxContractsPerCustomer\": 500, \"maxConcurrentReplayJobs\": 5 } }.")]
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
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Contracts.Alerts;

namespace NuntivusServer.Api.Controllers.Admin
{
    [ApiController]
    [Route("v1/alerts")]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    public sealed class AlertsController(IAlertService alerts, IAlertOperationsService operations) : ControllerBase
    {
        [HttpGet("rules")]
        public async Task<IActionResult> Rules(CancellationToken cancellationToken, [FromQuery] int page = 1) =>
            (await alerts.ListRulesAsync(page, cancellationToken)).ToOk(HttpContext);
        [HttpGet("notifications")]
        public async Task<IActionResult> Notifications(CancellationToken cancellationToken, [FromQuery] int page = 1) =>
            (await alerts.ListNotificationsAsync(page, cancellationToken)).ToOk(HttpContext);
        [HttpPut("rules/{id:guid}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Save(Guid id, [FromBody] AlertRuleRequest request, CancellationToken cancellationToken) =>
            (await alerts.SaveRuleAsync(id, request, Actor(), cancellationToken)).ToOk(HttpContext);
        [HttpPost("notifications/{id:guid}/acknowledge")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken) =>
            (await alerts.AcknowledgeAsync(id, Actor(), cancellationToken)).ToOk(HttpContext);
        [HttpDelete("rules/{id:guid}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken) =>
            (await operations.ArchiveRuleAsync(id, Actor(), cancellationToken)).ToOk(HttpContext);
        [HttpGet("notifications/{id:guid}/webhook")]
        public async Task<IActionResult> Webhook(Guid id, CancellationToken cancellationToken) =>
            (await operations.GetWebhookAsync(id, cancellationToken)).ToOk(HttpContext);
        [HttpGet("notifications/{id:guid}/webhook/attempts")]
        public async Task<IActionResult> Attempts(Guid id, CancellationToken cancellationToken, [FromQuery] int page = 1) =>
            (await operations.ListAttemptsAsync(id, page, cancellationToken)).ToOk(HttpContext);
        [HttpPost("notifications/{id:guid}/webhook/retry")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Retry(Guid id, [FromBody] AlertWebhookRetryRequest request, CancellationToken cancellationToken) =>
            (await operations.RetryWebhookAsync(id, request, Actor(), cancellationToken)).ToOk(HttpContext);
        private string Actor() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
    }
}

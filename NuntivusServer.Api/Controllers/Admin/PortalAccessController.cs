using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Portal;
using NuntivusServer.Contracts.Portal;
using NuntivusServer.Common;

namespace NuntivusServer.Api.Controllers.Admin
{
    [ApiController]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize(Policy = AdminAuthorizationPolicies.Admin)]
    [Route("v1/admin/portal")]
    public sealed class PortalAccessController(IPortalService portal) : ControllerBase
    {
        private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        [HttpPost("invitations")]
        public async Task<IActionResult> Invite(PortalInvitationRequest request, CancellationToken ct)
        {
            Response.Headers.CacheControl = "no-store";
            return (await portal.InviteAsync(request, Actor, ct)).ToOk(HttpContext);
        }
        [HttpDelete("invitations/{id:guid}")]
        public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken ct) => Map(await portal.RevokeInvitationAsync(id, Actor, ct));
        [HttpGet("customers/{customerId}/memberships")]
        public async Task<IActionResult> Memberships(string customerId, CancellationToken ct, int page = 1) => (await portal.MembershipsAsync(customerId, page, ct)).ToOk(HttpContext);
        [HttpDelete("customers/{customerId}/memberships/{accountId:guid}")]
        public async Task<IActionResult> Revoke(string customerId, Guid accountId, CancellationToken ct) => Map(await portal.RevokeAsync(accountId, customerId, Actor, ct));
        private IActionResult Map(Result result) => result.IsSuccess ? Ok() : Result<object>.Fail(result.Errors).ToOk(HttpContext);
    }
}

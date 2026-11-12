using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Portal;
using NuntivusServer.Contracts.Portal;

namespace NuntivusServer.Api.Controllers.Portal
{
    [ApiController]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Route("v1/portal")]
    public sealed class PortalController(IPortalService portal, PortalOptions options) : ControllerBase
    {
        [AllowAnonymous]
        [HttpGet("configuration")]
        public IActionResult Configuration() => Ok(new { options.LocalEnabled, options.ClerkEnabled, clerkAuthority = options.ClerkEnabled ? options.ClerkAuthority : null });

        [AllowAnonymous]
        [EnableRateLimiting(PortalAuthentication.RatePolicy)]
        [HttpPost("local/login")]
        public async Task<IActionResult> Login(PortalLoginRequest request, CancellationToken ct)
        {
            var result = await portal.LoginAsync(request.Username, request.Password, ct);
            return result.IsFailure ? result.ToOk(HttpContext) : Ok(PortalAuthentication.Token(result.Value, options));
        }
        [AllowAnonymous]
        [EnableRateLimiting(PortalAuthentication.RatePolicy)]
        [HttpPost("local/accept")]
        public async Task<IActionResult> AcceptLocal(PortalAcceptRequest request, CancellationToken ct)
        {
            if (!options.LocalEnabled) return Forbid();
            var result = await portal.AcceptAsync(request.Token, request.Password, null, ct);
            return result.IsFailure ? result.ToOk(HttpContext) : Ok(PortalAuthentication.Token(result.Value, options));
        }
        [Authorize(Policy = PortalAuthentication.Policy)]
        [EnableRateLimiting(PortalAuthentication.RatePolicy)]
        [HttpPost("invitations/accept")]
        public async Task<IActionResult> AcceptExternal(PortalAcceptRequest request, CancellationToken ct)
        {
            var identity = PortalAuthentication.Identity(User, options);
            if (identity.Issuer != options.ClerkAuthority || !options.ClerkEnabled) return Forbid();
            return (await portal.AcceptAsync(request.Token, null, identity, ct)).ToOk(HttpContext);
        }
        [Authorize(Policy = PortalAuthentication.Policy)]
        [HttpGet("customers")]
        public async Task<IActionResult> Customers(CancellationToken ct, int page = 1) =>
            (await portal.CustomersAsync(PortalAuthentication.Identity(User, options), page, ct)).ToOk(HttpContext);

        [Authorize(Policy = PortalAuthentication.Policy)]
        [HttpGet("customers/{customerId}/contracts")]
        public async Task<IActionResult> Contracts(string customerId, CancellationToken ct, int page = 1, Guid? id = null) =>
            (await portal.ContractsAsync(PortalAuthentication.Identity(User, options), customerId, page, id, ct)).ToOk(HttpContext);

        [Authorize(Policy = PortalAuthentication.Policy)]
        [HttpGet("customers/{customerId}/deliveries")]
        public async Task<IActionResult> Deliveries(string customerId, CancellationToken ct, int page = 1, Guid? id = null) =>
            (await portal.DeliveriesAsync(PortalAuthentication.Identity(User, options), customerId, page, id, ct)).ToOk(HttpContext);
    }
}

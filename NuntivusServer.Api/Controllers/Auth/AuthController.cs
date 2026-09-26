using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Security;
using NuntivusServer.Common;
using Microsoft.AspNetCore.Mvc;

namespace NuntivusServer.Api.Controllers.Auth
{
    [ApiController]
    [Route("v1/auth")]
    public sealed class AuthController : ControllerBase
    {
        private readonly IAdminAuthenticationProvider _authenticationProvider;
        private readonly IAdminTokenIssuer _tokenIssuer;
        private readonly LocalAuthOption _localAuth;

        public AuthController(
            IAdminAuthenticationProvider authenticationProvider,
            IAdminTokenIssuer tokenIssuer,
            LocalAuthOption localAuth)
        {
            _authenticationProvider = authenticationProvider;
            _tokenIssuer = tokenIssuer;
            _localAuth = localAuth;
        }

        [AllowAnonymous]
        [HttpPost("local/login")]
        public async Task<IActionResult> LocalLogin(
            [FromBody] LocalLoginRequest request,
            CancellationToken cancellationToken)
        {
            if (!_localAuth.Enabled)
            {
                var disabled = ApiErrorResponse.From(
                    [Error.Forbidden("Local authentication is disabled.")],
                    HttpContext,
                    sanitizeUnexpected: false);
                return new ObjectResult(disabled)
                {
                    StatusCode = disabled.Status,
                };
            }

            if (request is null
                || string.IsNullOrWhiteSpace(request.Username)
                || string.IsNullOrWhiteSpace(request.Password))
            {
                var validation = ApiErrorResponse.From(
                    [Error.Validation("username and password are required.")],
                    HttpContext,
                    sanitizeUnexpected: false);
                return new ObjectResult(validation)
                {
                    StatusCode = validation.Status,
                };
            }

            var auth = await _authenticationProvider.AuthenticateAsync(
                request.Username,
                request.Password,
                cancellationToken);

            if (auth.IsFailure)
            {
                var payload = ApiErrorResponse.From(auth.Errors, HttpContext, sanitizeUnexpected: false);
                return new ObjectResult(payload)
                {
                    StatusCode = payload.Status,
                };
            }

            var now = DateTimeOffset.UtcNow;
            var accessToken = _tokenIssuer.IssueToken(auth.Value, now);
            return Ok(new LocalLoginResponse(
                accessToken,
                "Bearer",
                now.Add(_localAuth.AccessTokenLifetime),
                _localAuth.AccessTokenLifetime.TotalSeconds));
        }

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

        public sealed record LocalLoginRequest(string Username, string Password);

        public sealed record LocalLoginResponse(
            string AccessToken,
            string TokenType,
            DateTimeOffset ExpiresAt,
            double ExpiresInSeconds);
    }
}
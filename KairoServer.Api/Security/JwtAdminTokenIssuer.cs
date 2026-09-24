using System;
using System.Collections.Generic;
using System.Security.Claims;
using KairoServer.Application.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KairoServer.Api.Security
{
    public sealed class JwtAdminTokenIssuer : IAdminTokenIssuer
    {
        private readonly LocalAuthOption _options;

        public JwtAdminTokenIssuer(LocalAuthOption options)
        {
            _options = options;
        }

        public string IssueToken(AdminAuthenticatedIdentity identity, DateTimeOffset issuedAtUtc)
        {
            var expiresAt = issuedAtUtc.Add(_options.AccessTokenLifetime);
            var signingKey = _options.CreateSigningKey();
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, identity.Subject),
            };

            if (!string.IsNullOrWhiteSpace(identity.DisplayName))
            {
                claims.Add(new Claim(ClaimTypes.Name, identity.DisplayName));
                claims.Add(new Claim(TokenClaimTypes.Name, identity.DisplayName));
            }

            foreach (var role in identity.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
                claims.Add(new Claim(TokenClaimTypes.Role, role));
            }

            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                NotBefore = issuedAtUtc.UtcDateTime,
                IssuedAt = issuedAtUtc.UtcDateTime,
                Expires = expiresAt.UtcDateTime,
                SigningCredentials = credentials,
            };

            var handler = new JsonWebTokenHandler();
            return handler.CreateToken(descriptor);
        }
    }
}

using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using NuntivusServer.Application.Portal;
using NuntivusServer.Contracts.Portal;
using NuntivusServer.Infastructure.Persistence.Postgres;
using NuntivusServer.Infastructure.Security;

namespace NuntivusServer.Api.Security
{
    public static class PortalAuthentication
    {
        public const string LocalScheme = "PortalLocal";
        public const string ClerkScheme = "PortalClerk";
        public const string Policy = "Portal";
        public const string RatePolicy = "portal-enrollment";
        public static IServiceCollection AddPortal(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<PortalOptions>().Bind(configuration.GetSection("Portal"))
                .Validate(options => !options.LocalEnabled || LocalAuthOption.HasValidSigningKey(options.SigningKey)
                    && !string.IsNullOrWhiteSpace(options.LocalIssuer) && !string.IsNullOrWhiteSpace(options.LocalAudience), "Configure Portal local issuer, audience and signing key.")
                .Validate(options => !options.ClerkEnabled || Uri.TryCreate(options.ClerkAuthority, UriKind.Absolute, out var authority) && authority.Scheme == "https"
                    && string.IsNullOrEmpty(authority.UserInfo) && string.IsNullOrEmpty(authority.Fragment) && string.IsNullOrEmpty(authority.Query)
                    && !string.IsNullOrWhiteSpace(options.ClerkAudience) && options.ClerkAuthorizedParties.Length > 0, "Configure Clerk HTTPS authority, audience and authorized parties.")
                .Validate(options => !options.LocalEnabled || !options.ClerkEnabled || options.LocalIssuer != options.ClerkAuthority, "Portal issuers must be distinct.")
                .ValidateOnStart();
            services.AddSingleton(sp => sp.GetRequiredService<IOptions<PortalOptions>>().Value);
            services.AddSingleton<IPortalIdentityProvider, LocalPortalIdentityProvider>();
            services.AddSingleton<IPortalIdentityProvider, ClerkPortalIdentityProvider>();
            services.AddScoped<IPortalService, PostgresPortalService>();
            services.AddAuthentication().AddJwtBearer(LocalScheme, _ => { }).AddJwtBearer(ClerkScheme, _ => { });
            services.AddOptions<JwtBearerOptions>(LocalScheme).Configure<PortalOptions>((jwt, options) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = options.LocalIssuer,
                    ValidateAudience = true, ValidAudience = options.LocalAudience,
                    ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(options.LocalEnabled ? Convert.FromBase64String(options.SigningKey) : System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
                };
                jwt.Events = new JwtBearerEvents { OnTokenValidated = context =>
                {
                    if (!options.LocalEnabled || !long.TryParse(context.Principal?.FindFirstValue("credential_version"), out var version) || version < 1)
                        context.Fail("Portal local authentication is unavailable.");
                    return Task.CompletedTask;
                } };
            });
            services.AddOptions<JwtBearerOptions>(ClerkScheme).Configure<PortalOptions>((jwt, options) =>
            {
                jwt.MapInboundClaims = false;
                jwt.RequireHttpsMetadata = true;
                if (options.ClerkEnabled) jwt.Authority = options.ClerkAuthority;
                jwt.Audience = options.ClerkAudience;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = options.ClerkAuthority, ValidateAudience = true, ValidAudience = options.ClerkAudience,
                    ValidateLifetime = true, RequireExpirationTime = true, ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.FromSeconds(30)
                };
                jwt.Events = new JwtBearerEvents { OnTokenValidated = context =>
                {
                    var party = context.Principal?.FindFirstValue("azp");
                    if (!options.ClerkEnabled || party is null || !options.ClerkAuthorizedParties.Contains(party, StringComparer.Ordinal))
                        context.Fail("Clerk authorized party is not allowed.");
                    return Task.CompletedTask;
                } };
            });
            services.AddAuthorization(auth => auth.AddPolicy(Policy, policy => policy.AddAuthenticationSchemes(LocalScheme, ClerkScheme).RequireAuthenticatedUser().RequireClaim("iss").RequireClaim("sub")));
            services.AddRateLimiter(rate =>
            {
                rate.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                rate.AddPolicy(RatePolicy, context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            });
            return services;
        }
        public static PortalIdentity Identity(ClaimsPrincipal principal, PortalOptions options)
        {
            var issuer = principal.FindFirstValue("iss") ?? throw new InvalidOperationException("Validated issuer is missing.");
            var subject = principal.FindFirstValue("sub") ?? throw new InvalidOperationException("Validated subject is missing.");
            long? version = issuer == options.LocalIssuer ? long.Parse(principal.FindFirstValue("credential_version")!, CultureInfo.InvariantCulture) : null;
            return new(issuer, subject, version);
        }
        public static object Token(PortalAccount account, PortalOptions options)
        {
            var now = DateTime.UtcNow;
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = options.LocalIssuer, Audience = options.LocalAudience, IssuedAt = now, NotBefore = now, Expires = now.AddMinutes(30),
                Claims = new Dictionary<string, object> { ["sub"] = account.Subject, ["credential_version"] = account.CredentialVersion.ToString(CultureInfo.InvariantCulture) },
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(options.SigningKey)), SecurityAlgorithms.HmacSha256)
            };
            return new { accessToken = new JsonWebTokenHandler().CreateToken(descriptor), tokenType = "Bearer", expiresAt = now.AddMinutes(30) };
        }
    }
}

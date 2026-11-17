using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

namespace NuntivusServer.Api.Security
{
    public sealed class RequestProtectionOptions
    {
        public int LoginPermitLimit { get; set; } = 10;
        public int IngestionPermitLimit { get; set; } = 6000;
        public int WindowSeconds { get; set; } = 60;
        public bool IsValid() => LoginPermitLimit > 0 && IngestionPermitLimit > 0
            && WindowSeconds is > 0 and <= 3600;
    }

    public static class ApiProtection
    {
        public const string LoginPolicy = "admin-login";
        public const string IngestionPolicy = "source-ingestion";

        public static bool HasProductionAuthentication(IConfiguration configuration)
        {
            var local = configuration.GetSection("LocalAuth").Get<LocalAuthOption>() ?? new();
            if (local.Enabled)
                return LocalAuthOption.HasValidSigningKey(local.SigningKey)
                    && !string.IsNullOrWhiteSpace(local.Issuer) && !string.IsNullOrWhiteSpace(local.Audience);
            return Uri.TryCreate(configuration["Authentication:Authority"], UriKind.Absolute, out var authority)
                && authority.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(authority.UserInfo)
                && !string.IsNullOrWhiteSpace(configuration["Authentication:Audience"]);
        }

        public static void AddProtection(this IServiceCollection services, IConfiguration configuration)
        {
            var limits = configuration.GetSection("RequestProtection").Get<RequestProtectionOptions>() ?? new();
            if (!limits.IsValid()) throw new InvalidOperationException("RequestProtection limits must be positive; WindowSeconds must be at most 3600.");
            services.AddRateLimiter(rate =>
            {
                rate.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                rate.AddPolicy(LoginPolicy, context => Partition(context, limits.LoginPermitLimit, limits.WindowSeconds, false));
                rate.AddPolicy(IngestionPolicy, context => Partition(context, limits.IngestionPermitLimit, limits.WindowSeconds, true));
                rate.OnRejected = async (rejection, token) =>
                {
                    if (rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                        rejection.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                    await rejection.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = 429, Title = "Too many requests.", Detail = "Retry after the rate-limit window expires.",
                        Extensions = { ["code"] = "rate_limited" }
                    }, cancellationToken: token);
                };
            });

            var proxies = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
            if (proxies.Any(value => !IPAddress.TryParse(value, out _)))
                throw new InvalidOperationException("ReverseProxy:KnownProxies must contain exact IP addresses.");
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                foreach (var value in proxies) options.KnownProxies.Add(IPAddress.Parse(value));
                // With no explicit trust, process no forwarded headers at all.
                if (proxies.Length == 0) options.ForwardedHeaders = ForwardedHeaders.None;
            });
            var httpsPort = configuration.GetValue("ReverseProxy:HttpsPort", 443);
            if (httpsPort is < 1 or > 65535) throw new InvalidOperationException("ReverseProxy:HttpsPort must be a valid port.");
            services.AddHttpsRedirection(options => options.HttpsPort = httpsPort);
        }

        private static RateLimitPartition<string> Partition(HttpContext context, int permits, int seconds, bool source)
        {
            var ip = context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";
            var key = source ? $"{ip}:{context.Request.RouteValues["id"]}" : ip;
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits, Window = TimeSpan.FromSeconds(seconds), QueueLimit = 0,
                AutoReplenishment = true
            });
        }
    }
}

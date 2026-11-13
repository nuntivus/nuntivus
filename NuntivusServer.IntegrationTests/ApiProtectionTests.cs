using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using NuntivusServer.Api.Security;

namespace NuntivusServer.IntegrationTests
{
    public sealed class ApiProtectionTests
    {
        [Fact]
        public async Task Login_is_limited_before_authentication()
        {
            await using var app = await StartAsync();
            using var client = app.GetTestClient();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/login", null)).StatusCode);
            var rejected = await client.PostAsync("/login", null);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.NotNull(rejected.Headers.RetryAfter);
            Assert.Contains("rate_limited", await rejected.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Ingestion_limits_are_separate_per_source_and_from_login()
        {
            await using var app = await StartAsync();
            using var client = app.GetTestClient();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/sources/one", null)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/sources/one", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/sources/two", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/login", null)).StatusCode);
        }

        [Theory]
        [InlineData(false, "http|127.0.0.1")]
        [InlineData(true, "https|203.0.113.1")]
        public async Task Forwarded_headers_require_explicit_proxy_trust(bool trusted, string expected)
        {
            await using var app = await StartAsync(trusted);
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.1");
            Assert.Equal(expected, await client.GetStringAsync("/identity"));
        }

        [Theory]
        [InlineData(null, null, false)]
        [InlineData("http://issuer.example", "api", false)]
        [InlineData("https://issuer.example", null, false)]
        [InlineData("https://issuer.example", "api", true)]
        public void Production_requires_an_HTTPS_authority_and_audience_when_local_auth_is_disabled(
            string? authority, string? audience, bool expected)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = authority, ["Authentication:Audience"] = audience
            }).Build();
            Assert.Equal(expected, ApiProtection.HasProductionAuthentication(configuration));
        }

        [Fact]
        public void Invalid_limits_fail_configuration()
        {
            Assert.False(new RequestProtectionOptions { LoginPermitLimit = 0 }.IsValid());
            Assert.False(new RequestProtectionOptions { IngestionPermitLimit = 0 }.IsValid());
            Assert.False(new RequestProtectionOptions { WindowSeconds = 0 }.IsValid());
        }

        private static async Task<WebApplication> StartAsync(bool trusted = false)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RequestProtection:LoginPermitLimit"] = "1", ["RequestProtection:IngestionPermitLimit"] = "1",
                ["ReverseProxy:KnownProxies:0"] = trusted ? "127.0.0.1" : null
            }.Where(pair => pair.Value is not null));
            builder.Services.AddProtection(builder.Configuration);
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                await next(context);
            });
            app.UseForwardedHeaders();
            app.UseRouting();
            app.UseRateLimiter();
            app.MapPost("/login", () => Results.Ok()).RequireRateLimiting(ApiProtection.LoginPolicy);
            app.MapPost("/sources/{id}", () => Results.Ok()).RequireRateLimiting(ApiProtection.IngestionPolicy);
            app.MapGet("/identity", (HttpContext context) => $"{context.Request.Scheme}|{context.Connection.RemoteIpAddress}");
            await app.StartAsync();
            return app;
        }
    }
}

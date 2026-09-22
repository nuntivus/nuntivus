using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Swashbuckle.AspNetCore.Annotations;

namespace KairoServer.Api.Controllers.Operations
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/ops")]
    public sealed class DiagnosticsController : ControllerBase
    {
        private readonly HealthCheckService _healthChecks;

        public DiagnosticsController(HealthCheckService healthChecks)
        {
            _healthChecks = healthChecks;
        }

        [HttpGet("diagnostics")]
        [SwaggerOperation(
            Summary = "Get operational diagnostics",
            Description = "Returns minimal operational diagnostics for FE support: timestamp, environment, app version, request/correlation IDs, and readiness summary.")]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var readiness = await _healthChecks.CheckHealthAsync(
                registration => registration.Tags.Contains("ready"),
                cancellationToken);

            var response = new DiagnosticsView(
                DateTimeOffset.UtcNow,
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
                HttpContext.TraceIdentifier,
                Request.Headers.TryGetValue(ApiErrorResponse.CorrelationIdHeader, out var correlation)
                    ? correlation.ToString()
                    : HttpContext.TraceIdentifier,
                readiness.Status.ToString());

            return Ok(response);
        }

        public sealed record DiagnosticsView(
            DateTimeOffset TimestampUtc,
            string Environment,
            string AppVersion,
            string RequestId,
            string CorrelationId,
            string ReadinessStatus);
    }
}

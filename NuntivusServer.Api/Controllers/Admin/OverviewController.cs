using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NuntivusServer.Api.Controllers.Admin
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/admin/overview")]
    public sealed class OverviewController : ControllerBase
    {
        private readonly IDashboardOverviewQueryService _overview;
        private readonly IDashboardTimeSeriesQueryService _timeSeries;

        public OverviewController(
            IDashboardOverviewQueryService overview,
            IDashboardTimeSeriesQueryService timeSeries)
        {
            _overview = overview;
            _timeSeries = timeSeries;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var result = await _overview.GetAsync(cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet("timeseries")]
        public async Task<IActionResult> GetTimeSeries(
            [FromQuery] int rangeSeconds,
            CancellationToken cancellationToken)
        {
            var result = await _timeSeries.GetAsync(rangeSeconds, cancellationToken);
            return result.ToOk(HttpContext);
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Api.Security;
using KairoServer.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Admin
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/admin/overview")]
    public sealed class OverviewController : ControllerBase
    {
        private readonly IDashboardOverviewQueryService _overview;

        public OverviewController(IDashboardOverviewQueryService overview)
        {
            _overview = overview;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var result = await _overview.GetAsync(cancellationToken);
            return result.ToOk(HttpContext);
        }
    }
}

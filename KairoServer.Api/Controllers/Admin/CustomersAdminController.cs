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
    [Route("v1/admin/customers")]
    public sealed class CustomersAdminController : ControllerBase
    {
        private readonly ICustomerAdminSummaryQueryService _summaries;

        public CustomersAdminController(ICustomerAdminSummaryQueryService summaries)
        {
            _summaries = summaries;
        }

        [HttpGet("{id}/summary")]
        public async Task<IActionResult> GetSummary(string id, CancellationToken cancellationToken)
        {
            var result = await _summaries.GetAsync(id, cancellationToken);
            return result.ToOk(HttpContext);
        }
    }
}

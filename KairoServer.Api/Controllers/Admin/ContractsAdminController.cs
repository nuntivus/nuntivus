using System;
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
    [Route("v1/admin/contracts")]
    public sealed class ContractsAdminController : ControllerBase
    {
        private readonly IContractAdminSummaryQueryService _summaries;

        public ContractsAdminController(IContractAdminSummaryQueryService summaries)
        {
            _summaries = summaries;
        }

        [HttpGet("{id:guid}/summary")]
        public async Task<IActionResult> GetSummary(Guid id, CancellationToken cancellationToken)
        {
            var result = await _summaries.GetAsync(id, cancellationToken);
            return result.ToOk(HttpContext);
        }
    }
}

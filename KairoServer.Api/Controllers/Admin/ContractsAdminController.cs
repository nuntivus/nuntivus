using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Api.Security;
using KairoServer.Application.Contracts;
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
        private readonly IContractService _contracts;

        public ContractsAdminController(
            IContractAdminSummaryQueryService summaries,
            IContractService contracts)
        {
            _summaries = summaries;
            _contracts = contracts;
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] AdminContractsListApiRequest request,
            CancellationToken cancellationToken)
        {
            var query = new AdminContractListQuery(
                request.CustomerId,
                request.Enabled,
                request.DestinationType,
                request.EventType,
                request.Search,
                request.CreatedFrom,
                request.CreatedTo,
                request.SortBy,
                request.SortDirection,
                request.Page,
                request.PageSize);

            var result = await _contracts.ListAsync(query, cancellationToken);
            return result.ToPagedOk(HttpContext);
        }

        [HttpGet("{id:guid}/summary")]
        public async Task<IActionResult> GetSummary(Guid id, CancellationToken cancellationToken)
        {
            var result = await _summaries.GetAsync(id, cancellationToken);
            return result.ToOk(HttpContext);
        }
    }
}

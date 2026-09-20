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
    [Route("v1/admin/deliveries")]
    public sealed class DeliveriesAdminController : ControllerBase
    {
        private readonly IAdminFailedDeliveryQueryService _failedDeliveries;

        public DeliveriesAdminController(IAdminFailedDeliveryQueryService failedDeliveries)
        {
            _failedDeliveries = failedDeliveries;
        }

        [HttpGet("failures")]
        public async Task<IActionResult> ListFailures(
            [FromQuery] AdminDeliveryFailuresApiRequest request,
            CancellationToken cancellationToken)
        {
            var query = new AdminFailedDeliveryListQuery(
                request.EventId,
                request.CustomerId,
                request.ContractId,
                request.CreatedFrom,
                request.CreatedTo,
                request.SortBy,
                request.SortDirection,
                request.Page,
                request.PageSize);

            var result = await _failedDeliveries.ListAsync(query, cancellationToken);
            return result.ToPagedOk(HttpContext);
        }
    }
}

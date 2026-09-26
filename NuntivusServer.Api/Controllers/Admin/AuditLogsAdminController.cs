using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NuntivusServer.Api.Controllers.Admin
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/admin/audit-logs")]
    public sealed class AuditLogsAdminController : ControllerBase
    {
        private readonly IAuditLogQueryService _auditLogs;

        public AuditLogsAdminController(IAuditLogQueryService auditLogs)
        {
            _auditLogs = auditLogs;
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] AuditLogsListApiRequest request,
            CancellationToken cancellationToken)
        {
            var query = new AuditLogListQuery(
                request.Category,
                request.Action,
                request.ActorId,
                request.CustomerId,
                request.ContractId,
                request.DeliveryId,
                request.EventId,
                request.OperationId,
                request.CreatedFrom,
                request.CreatedTo,
                request.SortDirection,
                request.Page,
                request.PageSize);

            var result = await _auditLogs.ListAsync(query, cancellationToken);
            return result.ToPagedOk(HttpContext);
        }
    }
}
using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Api.Security;
using KairoServer.Application.Deliveries.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Deliveries
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/destination-health")]
    public sealed class DestinationHealthController : ControllerBase
    {
        private readonly IDestinationHealthQueryService _queries;

        public DestinationHealthController(IDestinationHealthQueryService queries)
        {
            _queries = queries;
        }

        [HttpGet("contracts/{contractId:guid}")]
        public async Task<IActionResult> GetByContractId(Guid contractId, CancellationToken cancellationToken)
        {
            var result = await _queries.GetByContractIdAsync(contractId, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet("customers/{customerId}")]
        public async Task<IActionResult> ListByCustomerId(string customerId, CancellationToken cancellationToken)
        {
            var result = await _queries.ListByCustomerIdAsync(customerId, cancellationToken);
            return result.ToOk(HttpContext);
        }
    }
}

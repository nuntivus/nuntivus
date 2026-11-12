using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Api.Controllers.Contracts
{
    [ApiController]
    [Route("v1/contracts/{contractId:guid}/slo")]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    public sealed class ContractSloController(IContractSloService slos) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Get(Guid contractId, CancellationToken cancellationToken) =>
            (await slos.GetAsync(contractId, cancellationToken)).ToOk(HttpContext);

        [HttpPut]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Configure(Guid contractId, [FromBody] ContractSloPolicyRequest request, CancellationToken cancellationToken)
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
            return (await slos.ConfigureAsync(contractId, request, actor, cancellationToken)).ToOk(HttpContext);
        }
    }
}

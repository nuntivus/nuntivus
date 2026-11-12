using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Matching;
using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Api.Controllers.Contracts
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    public sealed class ContractDryRunController(IContractDryRunService dryRun) : ControllerBase
    {
        [HttpPost("v1/contracts/{contractId:guid}/dry-run")]
        public async Task<IActionResult> Saved(Guid contractId, [FromBody] ContractDryRunRequest request, CancellationToken cancellationToken)
        {
            var result = await dryRun.RunAsync(contractId, request, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPost("v1/contracts/dry-run")]
        public async Task<IActionResult> Draft([FromBody] ContractDryRunRequest request, CancellationToken cancellationToken)
        {
            var result = await dryRun.RunAsync(null, request, cancellationToken);
            return result.ToOk(HttpContext);
        }
    }
}

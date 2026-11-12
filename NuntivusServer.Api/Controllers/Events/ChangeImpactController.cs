using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Events;
using NuntivusServer.Contracts.Events;

namespace NuntivusServer.Api.Controllers.Events
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    public sealed class ChangeImpactController(IChangeImpactService impact) : ControllerBase
    {
        [HttpGet("v1/event-definitions/{eventType}/schemas/{version:int}/impact")]
        public async Task<IActionResult> Schema(string eventType, int version, CancellationToken cancellationToken,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int sampleSize = 10)
        {
            var result = await impact.AnalyzeSchemaAsync(eventType, version, page, pageSize, sampleSize, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPost("v1/contracts/{contractId:guid}/transformation-impact")]
        public async Task<IActionResult> Transformation(Guid contractId, [FromBody] TransformationImpactRequest request, CancellationToken cancellationToken)
        {
            var result = await impact.AnalyzeTransformationAsync(contractId, request, cancellationToken);
            return result.ToOk(HttpContext);
        }
    }
}

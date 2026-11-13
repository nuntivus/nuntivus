using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Deliveries.Queries;
using NuntivusServer.Application.Deliveries.Recovery;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Domain.Deliveries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace NuntivusServer.Api.Controllers.Deliveries
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/[controller]")]
    public sealed class DeliveriesController : Controller
    {
        private readonly IDeliveryRecoveryService _recovery;
        private readonly IDeliveryQueryService _queries;
        private readonly IAppLogger<DeliveriesController> _logger;

        public DeliveriesController(
            IDeliveryRecoveryService recovery,
            IDeliveryQueryService queries,
            IAppLogger<DeliveriesController> logger)
        {
            _recovery = recovery;
            _queries = queries;
            _logger = logger;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _queries.GetByIdAsync(id, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] DeliveryListApiRequest request,
            CancellationToken cancellationToken)
        {
            DeliveryStatus? status = null;
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<DeliveryStatus>(request.Status, ignoreCase: true, out var parsedStatus))
                {
                    return new BadRequestObjectResult(ApiErrorResponse.From(new[]
                    {
                        Error.Validation("status is invalid.")
                    }, HttpContext));
                }

                status = parsedStatus;
            }

            var query = new DeliveryListQuery(
                request.EventId,
                request.CustomerId,
                request.ContractId,
                request.DestinationType,
                request.FailureCategory,
                status,
                request.CreatedFrom,
                request.CreatedTo,
                request.SortBy,
                request.SortDirection,
                request.Page,
                request.PageSize,
                request.Search);

            var result = await _queries.ListAsync(query, cancellationToken);
            return result.ToPagedOk(HttpContext);
        }

        [HttpPost("{id:guid}/retry")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        [SwaggerOperation(
            Summary = "Retry failed delivery",
            Description = "Requests manual retry. Example request: { \"operationId\": \"8f3d6e6c-8dd6-4abc-9f48-8a9522537273\", \"reason\": \"Transient downstream outage recovered\" }.")]
        public async Task<IActionResult> Retry(
            Guid id,
            [FromBody] ManualDeliveryOperationApiRequest request,
            CancellationToken cancellationToken)
        {
            var operatorId = GetOperatorId();
            if (operatorId is null)
                return UnauthorizedResult();

            var operation = new ManualDeliveryOperationRequest(
                request.OperationId,
                operatorId,
                request.Reason,
                SerializeRequestSnapshot(id, "retry", request, operatorId));

            var result = await _recovery.ManualRetryAsync(id, operation, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPost("{id:guid}/requeue")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Requeue(
            Guid id,
            [FromBody] ManualDeliveryOperationApiRequest request,
            CancellationToken cancellationToken)
        {
            var operatorId = GetOperatorId();
            if (operatorId is null)
                return UnauthorizedResult();

            var operation = new ManualDeliveryOperationRequest(
                request.OperationId,
                operatorId,
                request.Reason,
                SerializeRequestSnapshot(id, "requeue", request, operatorId));

            var result = await _recovery.ManualRequeueAsync(id, operation, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPost("{id:guid}/cancel")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Cancel(
            Guid id,
            [FromBody] ManualDeliveryOperationApiRequest request,
            CancellationToken cancellationToken)
        {
            var operatorId = GetOperatorId();
            if (operatorId is null)
                return UnauthorizedResult();

            var operation = new ManualDeliveryOperationRequest(
                request.OperationId,
                operatorId,
                request.Reason,
                SerializeRequestSnapshot(id, "cancel", request, operatorId));

            var result = await _recovery.ManualCancelAsync(id, operation, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet("{id:guid}/failure")]
        public async Task<IActionResult> GetFailure(Guid id, CancellationToken cancellationToken)
        {
            var result = await _recovery.GetFailureAsync(id, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet("{id:guid}/attempts")]
        public async Task<IActionResult> GetAttempts(Guid id, CancellationToken cancellationToken)
        {
            var result = await _recovery.GetAttemptsAsync(id, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpPost("replay-jobs")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> CreateReplayJob(
            [FromBody] DeliveryReplayJobApiRequest request,
            CancellationToken cancellationToken)
        {
            var operatorId = GetOperatorId();
            if (operatorId is null)
                return UnauthorizedResult();

            var replayRequest = new DeliveryReplayRequest(
                request.OperationId,
                operatorId,
                SerializeReplayRequestSnapshot(request, operatorId),
                request.EventType,
                request.CustomerId,
                request.ContractId,
                request.CreatedFrom,
                request.CreatedTo,
                request.BatchLimit);

            var result = await _recovery.SubmitReplayJobAsync(replayRequest, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet("replay-jobs/{id:guid}")]
        public async Task<IActionResult> GetReplayJob(Guid id, CancellationToken cancellationToken)
        {
            var result = await _recovery.GetReplayJobAsync(id, cancellationToken);
            return result.ToOk(HttpContext);
        }

        private string? GetOperatorId()
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                _logger.Warning("Manual delivery operation rejected because principal is not authenticated.");
                return null;
            }

            return User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? User.Identity?.Name;
        }

        private IActionResult UnauthorizedResult() =>
            new ObjectResult(ApiErrorResponse.From(new[] { Error.Unauthorized("Authenticated operator principal is required.") }, HttpContext))
            {
                StatusCode = 401
            };

        private static string SerializeRequestSnapshot(
            Guid deliveryId,
            string operation,
            ManualDeliveryOperationApiRequest request,
            string operatorId)
        {
            return JsonSerializer.Serialize(new
            {
                deliveryId,
                operation,
                request.OperationId,
                request.Reason,
                operatorId,
                receivedAt = DateTimeOffset.UtcNow,
            });
        }

        private static string SerializeReplayRequestSnapshot(
            DeliveryReplayJobApiRequest request,
            string operatorId)
        {
            return JsonSerializer.Serialize(new
            {
                request.OperationId,
                request.EventType,
                request.CustomerId,
                request.ContractId,
                request.CreatedFrom,
                request.CreatedTo,
                request.BatchLimit,
                request.Reason,
                operatorId,
                receivedAt = DateTimeOffset.UtcNow,
            });
        }
    }
}

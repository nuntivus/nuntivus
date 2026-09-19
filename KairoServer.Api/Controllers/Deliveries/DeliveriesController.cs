using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Application.Deliveries.Recovery;
using KairoServer.Common;
using KairoServer.Common.Logging;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Deliveries
{
    [ApiController]
    [Route("v1/[controller]")]
    public sealed class DeliveriesController : Controller
    {
        private readonly IDeliveryRecoveryService _recovery;
        private readonly IAppLogger<DeliveriesController> _logger;

        public DeliveriesController(
            IDeliveryRecoveryService recovery,
            IAppLogger<DeliveriesController> logger)
        {
            _recovery = recovery;
            _logger = logger;
        }

        [HttpPost("{id:guid}/retry")]
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
            return result.ToOk();
        }

        [HttpPost("{id:guid}/requeue")]
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
            return result.ToOk();
        }

        [HttpPost("{id:guid}/cancel")]
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
            return result.ToOk();
        }

        [HttpGet("{id:guid}/failure")]
        public async Task<IActionResult> GetFailure(Guid id, CancellationToken cancellationToken)
        {
            var result = await _recovery.GetFailureAsync(id, cancellationToken);
            return result.ToOk();
        }

        [HttpGet("{id:guid}/attempts")]
        public async Task<IActionResult> GetAttempts(Guid id, CancellationToken cancellationToken)
        {
            var result = await _recovery.GetAttemptsAsync(id, cancellationToken);
            return result.ToOk();
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
            new ObjectResult(ApiErrorResponse.From(new[] { Error.Unauthorized("Authenticated operator principal is required.") }))
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
    }
}

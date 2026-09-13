using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Application.Contracts;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Contracts
{
    [ApiController]
    [Route("v1/[controller]")]
    public class ContractsController : Controller
    {
        private readonly IContractService _contracts;
        private readonly IAppLogger<ContractsController> _logger;

        public ContractsController(
            IContractService contracts,
            IAppLogger<ContractsController> logger)
        {
            _contracts = contracts;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] CreateContractRequest request,
            CancellationToken cancellationToken)
        {
            _logger.Information(
                "Received CreateContract request for customer {CustomerId}.",
                request?.CustomerId);

            var result = await _contracts.CreateAsync(request!, cancellationToken);

            return result.ToActionResult(created =>
                CreatedAtAction(nameof(GetById), new { id = created.Id }, created));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _contracts.GetByIdAsync(id, cancellationToken);
            return result.ToOk();
        }

        [HttpPost("{id:guid}/enable")]
        public async Task<IActionResult> Enable(Guid id, CancellationToken cancellationToken)
        {
            var result = await _contracts.EnableAsync(id, cancellationToken);
            return result.ToOk();
        }

        [HttpPost("{id:guid}/disable")]
        public async Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken)
        {
            var result = await _contracts.DisableAsync(id, cancellationToken);
            return result.ToOk();
        }

        [HttpPost("{id:guid}/credentials")]
        public async Task<IActionResult> UpdateCredentials(
            Guid id,
            [FromBody] UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _contracts.UpdateCredentialsAsync(id, request, cancellationToken);
            return result.ToOk();
        }

        [HttpPost("{id:guid}/credentials/rotate")]
        public async Task<IActionResult> RotateCredentials(
            Guid id,
            [FromBody] UpdateContractCredentialsRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _contracts.RotateCredentialsAsync(id, request, cancellationToken);
            return result.ToOk();
        }
    }
}

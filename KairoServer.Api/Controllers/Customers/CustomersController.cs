using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using KairoServer.Api.Common;
using KairoServer.Api.Controllers.Contracts;
using KairoServer.Api.Security;
using KairoServer.Application.Contracts;
using KairoServer.Application.Customers;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Contracts.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Customers
{
    [ApiController]
    [Route("v1/[controller]")]
    public class CustomersController : Controller
    {
        private readonly ICustomerService _customers;
        private readonly IContractService _contracts;
        private readonly IAppLogger<CustomersController> _logger;

        public CustomersController(
            ICustomerService customers,
            IContractService contracts,
            IAppLogger<CustomersController> logger)
        {
            _customers = customers;
            _contracts = contracts;
            _logger = logger;
        }

        [HttpPost]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Create(
            [FromBody] CreateCustomerRequest request,
            CancellationToken cancellationToken)
        {
            _logger.Information("Received CreateCustomer request (id {Id}).", request.Id);

            var result = await _customers.CreateAsync(request.Id, request.Name, cancellationToken, GetActorId());

            return result.ToActionResult(HttpContext, created =>
                CreatedAtAction(nameof(GetById), new { id = created.Id }, created));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
        {
            var result = await _customers.GetByIdAsync(id, cancellationToken);
            return result.ToOk(HttpContext);
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] CustomerListApiRequest request,
            CancellationToken cancellationToken)
        {
            var query = new CustomerListQuery(
                request.Enabled,
                request.Search,
                request.CreatedFrom,
                request.CreatedTo,
                request.SortBy,
                request.SortDirection,
                request.Page,
                request.PageSize);

            var result = await _customers.ListAsync(query, cancellationToken);

            return result.ToActionResult(HttpContext, paged =>
            {
                var body = new PagedResponse<CustomerView>(
                    paged.Items,
                    paged.Page,
                    paged.PageSize,
                    paged.TotalCount,
                    paged.TotalPages,
                    paged.HasPrevious,
                    paged.HasNext);
                return Ok(body);
            });
        }

        [HttpPost("{id}/enable")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Enable(string id, CancellationToken cancellationToken)
        {
            var result = await _customers.EnableAsync(id, cancellationToken, GetActorId());
            return result.ToOk(HttpContext);
        }

        [HttpPost("{id}/disable")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        public async Task<IActionResult> Disable(string id, CancellationToken cancellationToken)
        {
            var result = await _customers.DisableAsync(id, cancellationToken, GetActorId());
            return result.ToOk(HttpContext);
        }

        private string? GetActorId()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return null;

            return User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? User.Identity?.Name;
        }

        [HttpGet("{id}/contracts")]
        public async Task<IActionResult> ListContracts(
            string id,
            [FromQuery] ContractListApiRequest request,
            CancellationToken cancellationToken)
        {
            var query = new ContractListQuery(
                id,
                request.Enabled,
                request.EventType,
                request.CreatedFrom,
                request.CreatedTo,
                request.SortBy,
                request.SortDirection,
                request.Page,
                request.PageSize);

            var result = await _contracts.ListByCustomerAsync(query, cancellationToken);

            return result.ToActionResult(HttpContext, paged =>
            {
                var body = new PagedResponse<ContractView>(
                    paged.Items,
                    paged.Page,
                    paged.PageSize,
                    paged.TotalCount,
                    paged.TotalPages,
                    paged.HasPrevious,
                    paged.HasNext);
                return Ok(body);
            });
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Controllers.Contracts;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Customers;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Contracts.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace NuntivusServer.Api.Controllers.Customers
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
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
        [SwaggerOperation(
            Summary = "Create customer",
            Description = "Creates a customer. Example request: { \"id\": \"acme\", \"name\": \"Acme Corp\" }. Example response: { \"id\": \"acme\", \"name\": \"Acme Corp\", \"enabled\": true }.")]
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

        [HttpPut("{id}")]
        [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
        [SwaggerOperation(
            Summary = "Update customer",
            Description = "Updates customer information. Example request: { \"name\": \"Acme Corporation\" }.")]
        public async Task<IActionResult> Update(
            string id,
            [FromBody] UpdateCustomerRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _customers.UpdateAsync(id, request.Name, cancellationToken, GetActorId());
            return result.ToOk(HttpContext);
        }

        [HttpGet]
        [SwaggerOperation(
            Summary = "List customers",
            Description = "Returns paged customers using page/pageSize. Example: /v1/customers?page=1&pageSize=20&search=acme")]
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
            return result.ToPagedOk(HttpContext);
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
            return result.ToPagedOk(HttpContext);
        }
    }
}

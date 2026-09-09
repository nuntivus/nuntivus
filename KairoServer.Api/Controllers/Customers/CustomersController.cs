using System.Threading;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Application.Customers;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Customers;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Customers
{
    [ApiController]
    [Route("v1/[controller]")]
    public class CustomersController : Controller
    {
        private readonly ICustomerService _customers;
        private readonly IAppLogger<CustomersController> _logger;

        public CustomersController(
            ICustomerService customers,
            IAppLogger<CustomersController> logger)
        {
            _customers = customers;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] CreateCustomerRequest request,
            CancellationToken cancellationToken)
        {
            _logger.Information("Received CreateCustomer request (id {Id}).", request.Id);

            var result = await _customers.CreateAsync(request.Id, request.Name, cancellationToken);

            return result.ToActionResult(created =>
                CreatedAtAction(nameof(GetById), new { id = created.Id }, created));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
        {
            var result = await _customers.GetByIdAsync(id, cancellationToken);
            return result.ToOk();
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var result = await _customers.ListAsync(page, pageSize, cancellationToken);

            return result.ToActionResult(paged =>
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
        public async Task<IActionResult> Enable(string id, CancellationToken cancellationToken)
        {
            var result = await _customers.EnableAsync(id, cancellationToken);
            return result.ToOk();
        }

        [HttpPost("{id}/disable")]
        public async Task<IActionResult> Disable(string id, CancellationToken cancellationToken)
        {
            var result = await _customers.DisableAsync(id, cancellationToken);
            return result.ToOk();
        }
    }
}

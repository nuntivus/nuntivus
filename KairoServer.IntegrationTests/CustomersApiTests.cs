using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Api.Common;
using KairoServer.Api.Controllers.Customers;
using KairoServer.Contracts.Customers;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(CustomersApiCollection))]
    public sealed class CustomersApiTests : IAsyncLifetime
    {
        private readonly CustomersApiFixture _fixture;
        private readonly HttpClient _client;

        public CustomersApiTests(CustomersApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Create_returns_201_and_persists_customer()
        {
            var resp = await _client.PostAsJsonAsync("/v1/customers",
                new CreateCustomerRequest("acme", "Acme Corp"));

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<CustomerCreated>();
            Assert.NotNull(body);
            Assert.Equal("acme", body!.Id);
            Assert.True(body.Enabled);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM customers WHERE id = @id", new { id = "acme" });
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Create_with_invalid_id_returns_400_validation()
        {
            var resp = await _client.PostAsJsonAsync("/v1/customers",
                new CreateCustomerRequest("A!", "bad"));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("validation", err!.Code);
        }

        [Fact]
        public async Task Create_with_duplicate_id_returns_409_conflict()
        {
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("dup", "First"));
            var resp = await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("dup", "Second"));

            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("conflict", err!.Code);
        }

        [Fact]
        public async Task GetById_returns_customer()
        {
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("get1", "Get One"));

            var resp = await _client.GetAsync("/v1/customers/get1");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var view = await resp.Content.ReadFromJsonAsync<CustomerView>();
            Assert.Equal("get1", view!.Id);
            Assert.Equal("Get One", view.Name);
        }

        [Fact]
        public async Task GetById_missing_returns_404()
        {
            var resp = await _client.GetAsync("/v1/customers/missing");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task List_returns_paginated_results_with_defaults()
        {
            for (var i = 0; i < 3; i++)
                await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest($"c{i}", $"Cust {i}"));

            var resp = await _client.GetAsync("/v1/customers");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<PagedResponse<CustomerView>>();
            Assert.NotNull(body);
            Assert.Equal(1, body!.Page);
            Assert.Equal(20, body.PageSize);
            Assert.Equal(3, body.TotalCount);
            Assert.Equal(3, body.Items.Count);
            Assert.False(body.HasNext);
            Assert.False(body.HasPrevious);
        }

        [Fact]
        public async Task List_with_custom_paging_returns_page_slice()
        {
            for (var i = 0; i < 5; i++)
                await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest($"p{i}", $"P {i}"));

            var resp = await _client.GetAsync("/v1/customers?page=2&pageSize=2");
            var body = await resp.Content.ReadFromJsonAsync<PagedResponse<CustomerView>>();

            Assert.Equal(2, body!.Page);
            Assert.Equal(2, body.PageSize);
            Assert.Equal(5, body.TotalCount);
            Assert.Equal(3, body.TotalPages);
            Assert.Equal(2, body.Items.Count);
            Assert.True(body.HasPrevious);
            Assert.True(body.HasNext);
        }

        [Theory]
        [InlineData(0, 20)]
        [InlineData(1, 0)]
        [InlineData(1, 101)]
        public async Task List_with_invalid_paging_returns_400(int page, int pageSize)
        {
            var resp = await _client.GetAsync($"/v1/customers?page={page}&pageSize={pageSize}");
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Disable_then_Enable_toggles_state()
        {
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("tog", "Toggle"));

            var disable = await _client.PostAsync("/v1/customers/tog/disable", null);
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
            var disabled = await disable.Content.ReadFromJsonAsync<CustomerView>();
            Assert.False(disabled!.Enabled);

            var enable = await _client.PostAsync("/v1/customers/tog/enable", null);
            var enabled = await enable.Content.ReadFromJsonAsync<CustomerView>();
            Assert.True(enabled!.Enabled);
        }

        [Fact]
        public async Task Enable_missing_returns_404()
        {
            var resp = await _client.PostAsync("/v1/customers/missing/enable", null);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }
}

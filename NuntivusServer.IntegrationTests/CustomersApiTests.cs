using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Dapper;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Controllers.Customers;
using NuntivusServer.Contracts.Customers;
using Npgsql;

namespace NuntivusServer.IntegrationTests
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
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
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
        public async Task Create_propagates_correlation_id_header_on_success_response()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/customers")
            {
                Content = JsonContent.Create(new CreateCustomerRequest("corr-customer", "Correlation Customer"))
            };
            request.Headers.Add(ApiErrorResponse.CorrelationIdHeader, "corr-success-123");

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.True(response.Headers.TryGetValues(ApiErrorResponse.CorrelationIdHeader, out var values));
            Assert.Equal("corr-success-123", values!.Single());
        }

        [Fact]
        public async Task Create_and_disable_write_audit_entries()
        {
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("audit-cust", "Audit Customer"));
            await _client.PostAsync("/v1/customers/audit-cust/disable", null);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var actions = (await conn.QueryAsync<string>(
                "SELECT action FROM audit_logs WHERE category = 'customer' AND customer_id = @customerId ORDER BY created_at;",
                new { customerId = "audit-cust" })).AsList();

            Assert.Equal(2, actions.Count);
            Assert.Equal("created", actions[0]);
            Assert.Equal("disabled", actions[1]);
        }

        [Fact]
        public async Task Create_with_invalid_id_returns_400_validation()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/customers")
            {
                Content = JsonContent.Create(new CreateCustomerRequest("A!", "bad"))
            };
            request.Headers.Add(ApiErrorResponse.CorrelationIdHeader, "corr-validation");

            var resp = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("validation", err!.Code);
            Assert.Equal(400, err.Status);
            Assert.Equal("Validation failed.", err.Title);
            Assert.True(err.Extensions.ContainsKey("requestId"));
            Assert.Equal("corr-validation", err.Extensions["correlationId"]?.ToString());

            var payload = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.True(payload.RootElement.TryGetProperty("errors", out var errors));
            Assert.True(errors.GetArrayLength() > 0);
        }

        [Fact]
        public async Task Create_with_duplicate_id_returns_409_conflict()
        {
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("dup", "First"));
            var resp = await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("dup", "Second"));

            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("conflict", err!.Code);
            Assert.Equal(409, err.Status);
            Assert.Equal("Conflict.", err.Title);
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
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("not_found", err!.Code);
            Assert.Equal(404, err.Status);
            Assert.Equal("Resource not found.", err.Title);
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

        [Fact]
        public async Task List_supports_filter_sort_and_time_range()
        {
            var now = DateTimeOffset.UtcNow;

            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("acme", "Acme"));
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("beta", "Beta"));
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("gamma", "Gamma"));
            await _client.PostAsync("/v1/customers/beta/disable", null);

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE customers
                    SET created_at = CASE id
                        WHEN 'acme' THEN @AcmeCreatedAt
                        WHEN 'beta' THEN @BetaCreatedAt
                        WHEN 'gamma' THEN @GammaCreatedAt
                        ELSE created_at
                    END
                    WHERE id IN ('acme', 'beta', 'gamma');
                    """,
                    new
                    {
                        AcmeCreatedAt = now.AddMinutes(-25),
                        BetaCreatedAt = now.AddMinutes(-20),
                        GammaCreatedAt = now.AddMinutes(-15)
                    });
            }

            var url =
                "/v1/customers?enabled=true&search=a" +
                $"&createdFrom={Uri.EscapeDataString(now.AddMinutes(-26).ToString("O"))}" +
                $"&createdTo={Uri.EscapeDataString(now.AddMinutes(-14).ToString("O"))}" +
                "&sortBy=name&sortDirection=asc&page=1&pageSize=10";

            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<PagedResponse<CustomerView>>();
            Assert.NotNull(body);
            Assert.Equal(new[] { "acme", "gamma" }, body!.Items.Select(x => x.Id).ToArray());
        }

        [Fact]
        public async Task List_with_createdAt_sort_uses_stable_id_tiebreaker()
        {
            var now = DateTimeOffset.UtcNow;

            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("stable-b", "Stable B"));
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("stable-a", "Stable A"));

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE customers
                    SET created_at = @CreatedAt
                    WHERE id IN ('stable-a', 'stable-b');
                    """,
                    new { CreatedAt = now.AddMinutes(-10) });
            }

            var body = await _client.GetFromJsonAsync<PagedResponse<CustomerView>>(
                "/v1/customers?search=stable&sortBy=createdAt&sortDirection=asc&page=1&pageSize=10");

            Assert.NotNull(body);
            Assert.Equal(new[] { "stable-a", "stable-b" }, body!.Items.Select(x => x.Id).Take(2).ToArray());
        }

        [Theory]
        [InlineData("/v1/customers?sortBy=invalid")]
        [InlineData("/v1/customers?sortDirection=sideways")]
        [InlineData("/v1/customers?createdFrom=2026-01-02T00:00:00Z&createdTo=2026-01-01T00:00:00Z")]
        public async Task List_with_invalid_query_conventions_returns_400(string url)
        {
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
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
        public async Task Disable_when_already_disabled_is_idempotent_and_does_not_duplicate_audit()
        {
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("idem-disable", "Idem Disable"));

            var first = await _client.PostAsync("/v1/customers/idem-disable/disable", null);
            var second = await _client.PostAsync("/v1/customers/idem-disable/disable", null);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            var secondBody = await second.Content.ReadFromJsonAsync<CustomerView>();
            Assert.NotNull(secondBody);
            Assert.False(secondBody!.Enabled);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var actions = (await conn.QueryAsync<string>(
                "SELECT action FROM audit_logs WHERE category = 'customer' AND customer_id = @customerId ORDER BY created_at;",
                new { customerId = "idem-disable" })).AsList();

            Assert.Equal(2, actions.Count);
            Assert.Equal("created", actions[0]);
            Assert.Equal("disabled", actions[1]);
        }

        [Fact]
        public async Task Enable_when_already_enabled_is_idempotent_and_does_not_duplicate_audit()
        {
            await _client.PostAsJsonAsync("/v1/customers", new CreateCustomerRequest("idem-enable", "Idem Enable"));

            var response = await _client.PostAsync("/v1/customers/idem-enable/enable", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CustomerView>();
            Assert.NotNull(body);
            Assert.True(body!.Enabled);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var actions = (await conn.QueryAsync<string>(
                "SELECT action FROM audit_logs WHERE category = 'customer' AND customer_id = @customerId ORDER BY created_at;",
                new { customerId = "idem-enable" })).AsList();

            Assert.Single(actions);
            Assert.Equal("created", actions[0]);
        }

        [Fact]
        public async Task Enable_missing_returns_404()
        {
            var resp = await _client.PostAsync("/v1/customers/missing/enable", null);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Api.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Contracts.Customers;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(ContractsApiCollection))]
    public sealed class ContractsApiTests : IAsyncLifetime
    {
        private readonly ContractsApiFixture _fixture;
        private readonly HttpClient _client;

        public ContractsApiTests(ContractsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        private async Task CreateCustomerAsync(string id)
        {
            var resp = await _client.PostAsJsonAsync("/v1/customers",
                new { Id = id, Name = id });
            resp.EnsureSuccessStatusCode();
        }

        private static CreateContractRequest ValidRequest(
            string customerId,
            params string[] eventTypes) =>
            new(
                customerId,
                eventTypes.Length == 0 ? new[] { "orders.created" } : eventTypes,
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("hmac", "secret.acme.webhook"),
                    Headers: null,
                    TimeoutSeconds: 30));

        [Fact]
        public async Task Create_returns_201_and_persists_contract_with_event_types()
        {
            await CreateCustomerAsync("acme");

            var resp = await _client.PostAsJsonAsync("/v1/contracts",
                ValidRequest("acme", "orders.created", "orders.updated"));

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.NotNull(body);
            Assert.Equal("acme", body!.CustomerId);
            Assert.True(body.Enabled);
            Assert.Equal(2, body.EventTypes.Count);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var contractCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM contracts WHERE id = @id", new { id = body.Id });
            Assert.Equal(1, contractCount);

            var etCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM contract_event_types WHERE contract_id = @id",
                new { id = body.Id });
            Assert.Equal(2, etCount);
        }

        [Fact]
        public async Task Create_for_missing_customer_returns_404()
        {
            var resp = await _client.PostAsJsonAsync("/v1/contracts",
                ValidRequest("ghost"));

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("not_found", err!.Code);
        }

        [Fact]
        public async Task Create_without_event_types_returns_400()
        {
            await CreateCustomerAsync("acme");

            var resp = await _client.PostAsJsonAsync("/v1/contracts",
                new CreateContractRequest(
                    "acme",
                    Array.Empty<string>(),
                    new HttpDestinationConfig(
                        "https://x.example.com",
                        new HttpAuthentication("none", null))));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_with_invalid_url_returns_400()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "not-a-url",
                    new HttpAuthentication("none", null)));

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_with_invalid_secret_ref_scheme_returns_400()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://ok.example.com",
                    new HttpAuthentication("hmac", "plainstring")));

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_with_unknown_auth_type_returns_400()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://ok.example.com",
                    new HttpAuthentication("basic", "secret.x.y")));

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_with_invalid_event_type_returns_400()
        {
            await CreateCustomerAsync("acme");

            var req = ValidRequest("acme", "BAD EVENT!");
            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task GetById_returns_contract()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme"));
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();

            var resp = await _client.GetAsync($"/v1/contracts/{created!.Id}");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var view = await resp.Content.ReadFromJsonAsync<ContractView>();
            Assert.Equal(created.Id, view!.Id);
            Assert.Equal("acme", view.CustomerId);
            Assert.Contains("orders.created", view.EventTypes);

            using var config = JsonDocument.Parse(view.DestinationConfiguration);
            Assert.Equal("https://hooks.example.com/acme", config.RootElement.GetProperty("Url").GetString());
        }

        [Fact]
        public async Task GetById_missing_returns_404()
        {
            var resp = await _client.GetAsync($"/v1/contracts/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task ListByCustomer_returns_paginated_results()
        {
            await CreateCustomerAsync("acme");
            for (var i = 0; i < 3; i++)
                await _client.PostAsJsonAsync("/v1/contracts",
                    ValidRequest("acme", $"orders.evt{i}"));

            var resp = await _client.GetAsync("/v1/customers/acme/contracts");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<PagedResponse<ContractView>>();
            Assert.NotNull(body);
            Assert.Equal(3, body!.TotalCount);
            Assert.Equal(3, body.Items.Count);
        }

        [Fact]
        public async Task Disable_then_Enable_toggles_state()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme"));
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();

            var disable = await _client.PostAsync($"/v1/contracts/{created!.Id}/disable", null);
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
            var disabled = await disable.Content.ReadFromJsonAsync<ContractView>();
            Assert.False(disabled!.Enabled);

            var enable = await _client.PostAsync($"/v1/contracts/{created.Id}/enable", null);
            var enabled = await enable.Content.ReadFromJsonAsync<ContractView>();
            Assert.True(enabled!.Enabled);
        }

        [Fact]
        public async Task Enable_missing_returns_404()
        {
            var resp = await _client.PostAsync($"/v1/contracts/{Guid.NewGuid()}/enable", null);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }
}

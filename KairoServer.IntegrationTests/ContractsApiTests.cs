using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Dapper;
using KairoServer.Api.Common;
using KairoServer.Api.Controllers.Events;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Contracts.Customers;
using KairoServer.Contracts.Events;
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
                    new HttpAuthentication("hmac", new HttpCredential("ref", "secret.acme.webhook")),
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
                        new HttpAuthentication("none"))));

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
                    new HttpAuthentication("none")));

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_with_filter_persists_rules_and_returns_filter()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                Filter: new ContractFilterRequest(
                    MetadataRules: new[]
                    {
                        new ContractFilterRuleRequest("tenant", ContractFilterOperator.Equals, Value: "acme"),
                    },
                    PayloadRules: new[]
                    {
                        new ContractFilterRuleRequest("order.amount", ContractFilterOperator.GreaterThan, Value: "100"),
                    }));

            var createResp = await _client.PostAsJsonAsync("/v1/contracts", req);

            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var created = await createResp.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.NotNull(created);
            Assert.NotNull(created!.Filter);
            Assert.Single(created.Filter!.MetadataRules);
            Assert.Single(created.Filter.PayloadRules);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM contract_filter_rules WHERE contract_id = @id;",
                new { id = created.Id });
            Assert.Equal(2, count);
        }

        [Fact]
        public async Task Create_with_transformation_persists_and_returns_transformation()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                Transformation: new ContractPayloadTransformationRequest(
                    1,
                    new[]
                    {
                        new ContractPayloadFieldMappingRequest("order.id", "payload.orderId"),
                        new ContractPayloadFieldMappingRequest("order.amount", "payload.amount")
                    }));

            var createResp = await _client.PostAsJsonAsync("/v1/contracts", req);

            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var created = await createResp.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.NotNull(created);
            Assert.NotNull(created!.Transformation);
            Assert.Equal(1, created.Transformation!.Version);
            Assert.Equal(2, created.Transformation.FieldMappings.Count);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var transformationJson = await conn.ExecuteScalarAsync<string?>(
                "SELECT transformation_config::text FROM contracts WHERE id = @id;",
                new { id = created.Id });

            Assert.False(string.IsNullOrWhiteSpace(transformationJson));
        }

        [Fact]
        public async Task Create_with_duplicate_transformation_targets_returns_400()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                Transformation: new ContractPayloadTransformationRequest(
                    1,
                    new[]
                    {
                        new ContractPayloadFieldMappingRequest("order.id", "payload.orderId"),
                        new ContractPayloadFieldMappingRequest("order.externalId", "payload.orderId")
                    }));

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_with_invalid_filter_selector_returns_400()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                Filter: new ContractFilterRequest(
                    PayloadRules: new[]
                    {
                        new ContractFilterRuleRequest("order[*].id", ContractFilterOperator.Equals, Value: "x")
                    }));

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_with_set_operator_without_values_returns_400()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                Filter: new ContractFilterRequest(
                    PayloadRules: new[]
                    {
                        new ContractFilterRuleRequest("order.status", ContractFilterOperator.In, Value: null, Values: Array.Empty<string>())
                    }));

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_with_schema_targets_persists_targets()
        {
            await CreateCustomerAsync("acme");

            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.created", "Order Created", null));

            await _client.PostAsJsonAsync(
                "/v1/event-definitions/orders.created/schemas",
                new CreateEventSchemaVersionRequest(1, "{}", SchemaCompatibilityMode.Backward));

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                new[] { new ContractSchemaTargetRequest("orders.created", 1) });

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.NotNull(body);
            Assert.Single(body!.SchemaTargets);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM contract_schema_targets WHERE contract_id = @id",
                new { id = body.Id });

            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Create_with_missing_schema_target_returns_404()
        {
            await CreateCustomerAsync("acme");

            await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.created", "Order Created", null));

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                new[] { new ContractSchemaTargetRequest("orders.created", 99) });

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
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
                    new HttpAuthentication("hmac", new HttpCredential("ref", "plainstring"))));

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
                    new HttpAuthentication("basic", new HttpCredential("ref", "secret.x.y"))));

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
            Assert.Equal("***", config.RootElement.GetProperty("Authentication").GetProperty("Credential").GetProperty("Value").GetString());
        }

        [Fact]
        public async Task Create_with_inline_bearer_secret_persists_protected_and_get_redacts_secret()
        {
            await CreateCustomerAsync("acme");

            var inlineSecret = "bearer-inline-token";
            var createRequest = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("bearer", new HttpCredential("inline", inlineSecret)),
                    Headers: new Dictionary<string, string> { ["X-App"] = "kairo" },
                    SecretHeaders: new Dictionary<string, string> { ["X-Api-Key"] = "abc123" },
                    TimeoutSeconds: 30));

            var createResp = await _client.PostAsJsonAsync("/v1/contracts", createRequest);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var created = await createResp.Content.ReadFromJsonAsync<ContractCreated>();

            var getResp = await _client.GetAsync($"/v1/contracts/{created!.Id}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
            var view = await getResp.Content.ReadFromJsonAsync<ContractView>();

            using var redacted = JsonDocument.Parse(view!.DestinationConfiguration);
            Assert.Equal("***", redacted.RootElement.GetProperty("Authentication").GetProperty("Credential").GetProperty("Value").GetString());
            var secretHeaders = redacted.RootElement.GetProperty("SecretHeaders");
            Assert.Equal("***", secretHeaders.GetProperty("X-Api-Key").GetString());

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var storedConfig = await conn.ExecuteScalarAsync<string>(
                "SELECT destination_config::text FROM contracts WHERE id = @id;",
                new { id = created.Id });

            Assert.Contains("protected.", storedConfig);
            Assert.DoesNotContain(inlineSecret, storedConfig);
            Assert.DoesNotContain("abc123", storedConfig);
        }

        [Fact]
        public async Task Update_and_rotate_credentials_replace_stored_secret()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme"));
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();

            var updateResp = await _client.PostAsJsonAsync(
                $"/v1/contracts/{created!.Id}/credentials",
                new UpdateContractCredentialsRequest(new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("bearer", new HttpCredential("inline", "token-v1")),
                    TimeoutSeconds: 30)));
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var firstStored = await conn.ExecuteScalarAsync<string>(
                "SELECT destination_config::text FROM contracts WHERE id = @id;",
                new { id = created.Id });

            var rotateResp = await _client.PostAsJsonAsync(
                $"/v1/contracts/{created.Id}/credentials/rotate",
                new UpdateContractCredentialsRequest(new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("bearer", new HttpCredential("inline", "token-v2")),
                    TimeoutSeconds: 30)));
            Assert.Equal(HttpStatusCode.OK, rotateResp.StatusCode);
            var rotatedView = await rotateResp.Content.ReadFromJsonAsync<ContractView>();

            using var redacted = JsonDocument.Parse(rotatedView!.DestinationConfiguration);
            Assert.Equal("***", redacted.RootElement.GetProperty("Authentication").GetProperty("Credential").GetProperty("Value").GetString());

            var secondStored = await conn.ExecuteScalarAsync<string>(
                "SELECT destination_config::text FROM contracts WHERE id = @id;",
                new { id = created.Id });

            Assert.Contains("protected.", firstStored);
            Assert.Contains("protected.", secondStored);
            Assert.NotEqual(firstStored, secondStored);
            Assert.DoesNotContain("token-v1", secondStored);
            Assert.DoesNotContain("token-v2", secondStored);
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

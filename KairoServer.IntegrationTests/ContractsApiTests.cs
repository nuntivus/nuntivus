using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using KairoServer.Api.Common;
using KairoServer.Api.Controllers.Events;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Contracts.Customers;
using KairoServer.Contracts.Events;
using KairoServer.Domain.Outbox;
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
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
        }

        [Fact]
        public async Task Put_preserves_redacted_http_credentials_and_advanced_options()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", new CreateContractRequest(
                "acme", new[] { "orders.created" },
                new HttpDestinationConfig("https://hooks.example.com/acme",
                    new HttpAuthentication("bearer", new HttpCredential("ref", "secret.acme.webhook"))),
                Transformation: new ContractPayloadTransformationRequest(1,
                    new[] { new ContractPayloadFieldMappingRequest("order.id", "payload.orderId") }),
                RateLimit: new ContractRateLimitConfig(5)));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();
            var get = await _client.GetAsync($"/v1/contracts/{created!.Id}");
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var original = await get.Content.ReadFromJsonAsync<ContractView>();
            var redacted = JsonSerializer.Deserialize<HttpDestinationConfig>(original!.DestinationConfiguration);
            Assert.Equal("***", redacted!.Authentication.Credential!.Value);

            using var update = new HttpRequestMessage(HttpMethod.Put, $"/v1/contracts/{created.Id}")
            {
                Content = JsonContent.Create(new UpdateContractRequest(
                    "Renamed", original.EventTypes, redacted with { Url = "https://hooks.example.com/updated" },
                    original.SchemaTargets.Select(t => new ContractSchemaTargetRequest(t.EventType, t.Version)).ToArray(),
                    RateLimit: original.RateLimit,
                    Transformation: new ContractPayloadTransformationRequest(1,
                        new[] { new ContractPayloadFieldMappingRequest("order.id", "payload.orderId") })))
            };
            update.Headers.IfMatch.Add(get.Headers.ETag!);
            var put = await _client.SendAsync(update);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var body = await put.Content.ReadFromJsonAsync<ContractView>();
            Assert.Equal("Renamed", body!.Name);
            Assert.Equal(5, body.RateLimit!.PerSecond);
            Assert.Single(body.Transformation!.FieldMappings);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var persisted = await conn.ExecuteScalarAsync<string>(
                "SELECT destination_config::text FROM contracts WHERE id = @id;", new { id = created.Id });
            Assert.Contains("secret.acme.webhook", persisted);
            Assert.DoesNotContain("***", persisted);
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
        public async Task Test_event_preview_and_send_target_only_selected_contract()
        {
            await CreateCustomerAsync("test-target");
            var targetRequest = ValidRequest("test-target") with
            {
                Filter = new ContractFilterRequest(PayloadRules: new[]
                {
                    new ContractFilterRuleRequest("orderId", ContractFilterOperator.Equals, "matching")
                })
            };
            var targetResponse = await _client.PostAsJsonAsync("/v1/contracts", targetRequest);
            var target = await targetResponse.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.Equal(HttpStatusCode.Created, targetResponse.StatusCode);
            Assert.NotNull(target);
            var otherResponse = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("test-target"));
            var other = await otherResponse.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.Equal(HttpStatusCode.Created, otherResponse.StatusCode);
            Assert.NotNull(other);

            var mismatch = new ContractTestEventRequest(
                "orders.created", JsonSerializer.SerializeToElement(new { orderId = "other" }), null);
            var previewResponse = await _client.PostAsJsonAsync($"/v1/contracts/{target!.Id:D}/test/preview", mismatch);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = await previewResponse.Content.ReadFromJsonAsync<ContractTestPreviewView>();
            Assert.NotNull(preview);
            Assert.False(preview!.Matches);

            var rejected = await _client.PostAsJsonAsync($"/v1/contracts/{target.Id:D}/test/send", mismatch);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            Assert.Equal(0, await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM events WHERE source_id = @id;", new { id = target.Id }));

            var request = new ContractTestEventRequest(
                "orders.created", JsonSerializer.SerializeToElement(new { orderId = "matching" }),
                JsonSerializer.SerializeToElement(new { label = "smoke" }));
            var acceptedPreview = await _client.PostAsJsonAsync($"/v1/contracts/{target.Id:D}/test/preview", request);
            Assert.True((await acceptedPreview.Content.ReadFromJsonAsync<ContractTestPreviewView>())!.Matches);
            Assert.Equal(0, await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM events WHERE source_id = @id;", new { id = target.Id }));

            _client.DefaultRequestHeaders.Add("X-Operator-Id", "contract-test-operator");
            var sent = await _client.PostAsJsonAsync($"/v1/contracts/{target.Id:D}/test/send", request);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            var result = await sent.Content.ReadFromJsonAsync<ContractTestSendView>();
            Assert.NotNull(result);

            Assert.Equal(1, await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM deliveries WHERE event_id = @eventId AND contract_id = @contractId AND id = @deliveryId;",
                new { eventId = result!.EventId, contractId = target.Id, deliveryId = result.DeliveryId }));
            Assert.Equal(0, await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM deliveries WHERE event_id = @eventId AND contract_id = @contractId;",
                new { eventId = result.EventId, contractId = other!.Id }));
            Assert.Equal(1, await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM outbox_messages WHERE event_id = @eventId AND type = @type;",
                new { eventId = result.EventId, type = OutboxMessageType.DeliveryReady }));
            Assert.Equal(0, await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM outbox_messages WHERE event_id = @eventId AND type = @type;",
                new { eventId = result.EventId, type = OutboxMessageType.EventAccepted }));
            Assert.Equal("contract-test-operator", await conn.ExecuteScalarAsync<string>(
                "SELECT actor_id FROM audit_logs WHERE event_id = @eventId AND action = 'test_event_sent';",
                new { eventId = result.EventId }));
        }

        [Fact]
        public async Task Test_event_send_requires_operator_and_valid_payload()
        {
            await CreateCustomerAsync("test-auth");
            var created = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("test-auth"));
            var contract = await created.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.NotNull(contract);

            var invalid = new ContractTestEventRequest("orders.created", JsonSerializer.SerializeToElement("text"), null);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await _client.PostAsJsonAsync($"/v1/contracts/{contract!.Id:D}/test/preview", invalid)).StatusCode);

            using var viewer = _fixture.CreateClient();
            viewer.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            viewer.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            var valid = new ContractTestEventRequest("orders.created", JsonSerializer.SerializeToElement(new { id = 1 }), null);
            Assert.Equal(HttpStatusCode.OK,
                (await viewer.PostAsJsonAsync($"/v1/contracts/{contract.Id:D}/test/preview", valid)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await viewer.PostAsJsonAsync($"/v1/contracts/{contract.Id:D}/test/send", valid)).StatusCode);
        }

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
        public async Task Create_and_rotate_credentials_write_audit_entries()
        {
            await CreateCustomerAsync("audit-contract-customer");

            var createResponse = await _client.PostAsJsonAsync("/v1/contracts",
                ValidRequest("audit-contract-customer", "orders.created"));
            var created = await createResponse.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.NotNull(created);

            var rotateRequest = new UpdateContractCredentialsRequest(
                new HttpDestinationConfig(
                    "https://hooks.example.com/audit-rotated",
                    new HttpAuthentication("hmac", new HttpCredential("ref", "secret.acme.rotated")),
                    Headers: null,
                    TimeoutSeconds: 30));

            var rotateResponse = await _client.PostAsJsonAsync(
                $"/v1/contracts/{created!.Id:D}/credentials/rotate",
                rotateRequest);
            Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var actions = (await conn.QueryAsync<string>(
                "SELECT action FROM audit_logs WHERE category = 'contract' AND contract_id = @contractId ORDER BY created_at;",
                new { contractId = created.Id })).AsList();

            Assert.Equal(2, actions.Count);
            Assert.Equal("created", actions[0]);
            Assert.Equal("credentials_rotated", actions[1]);
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
        public async Task Create_with_too_many_event_types_returns_400()
        {
            await CreateCustomerAsync("acme");

            var eventTypes = Enumerable.Range(1, 101)
                .Select(i => $"orders.created.{i}")
                .ToArray();

            var resp = await _client.PostAsJsonAsync("/v1/contracts",
                new CreateContractRequest(
                    "acme",
                    eventTypes,
                    new HttpDestinationConfig(
                        "https://hooks.example.com/acme",
                        new HttpAuthentication("none"))));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("validation", err!.Code);
            Assert.Contains("at most", err.Message, StringComparison.OrdinalIgnoreCase);
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
        public async Task Create_with_invalid_rate_limit_returns_400()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://ok.example.com",
                    new HttpAuthentication("hmac", new HttpCredential("ref", "secret.acme.webhook"))),
                RateLimit: new ContractRateLimitConfig(0, 60, 10));

            var resp = await _client.PostAsJsonAsync("/v1/contracts", req);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.NotNull(err);
            Assert.Equal("validation", err!.Code);
        }

        [Fact]
        public async Task Create_with_private_network_destination_returns_400()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "http://127.0.0.1:8080/webhook",
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
        public async Task Create_with_rate_limit_persists_and_returns_rate_limit()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                RateLimit: new ContractRateLimitConfig(
                    PerSecond: 5,
                    PerMinute: 120,
                    Burst: 10));

            var createResp = await _client.PostAsJsonAsync("/v1/contracts", req);

            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var created = await createResp.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.NotNull(created);
            Assert.NotNull(created!.RateLimit);
            Assert.Equal(5, created.RateLimit!.PerSecond);
            Assert.Equal(120, created.RateLimit.PerMinute);
            Assert.Equal(10, created.RateLimit.Burst);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var row = await conn.QuerySingleAsync<(int? PerSecond, int? PerMinute, int? Burst)>(
                "SELECT rate_limit_per_second AS PerSecond, rate_limit_per_minute AS PerMinute, rate_limit_burst AS Burst FROM contracts WHERE id = @id;",
                new { id = created.Id });

            Assert.Equal(5, row.PerSecond);
            Assert.Equal(120, row.PerMinute);
            Assert.Equal(10, row.Burst);
        }

        [Fact]
        public async Task Create_with_ordering_persists_and_returns_ordering_config()
        {
            await CreateCustomerAsync("acme");

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                Ordering: new ContractOrderingConfig("order.id"));

            var createResp = await _client.PostAsJsonAsync("/v1/contracts", req);

            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var created = await createResp.Content.ReadFromJsonAsync<ContractCreated>();
            Assert.NotNull(created);
            Assert.NotNull(created!.Ordering);
            Assert.Equal("order.id", created.Ordering!.KeySelector);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var row = await conn.QuerySingleAsync<(bool OrderingEnabled, string? OrderingKeySelector)>(
                "SELECT ordering_enabled AS OrderingEnabled, ordering_key_selector AS OrderingKeySelector FROM contracts WHERE id = @id;",
                new { id = created.Id });

            Assert.True(row.OrderingEnabled);
            Assert.Equal("order.id", row.OrderingKeySelector);
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
        public async Task Rotate_hmac_credentials_persists_version_and_overlap_snapshot()
        {
            await CreateCustomerAsync("acme");

            var createResp = await _client.PostAsJsonAsync(
                "/v1/contracts",
                new CreateContractRequest(
                    "acme",
                    new[] { "orders.created" },
                    new HttpDestinationConfig(
                        "https://hooks.example.com/acme",
                        new HttpAuthentication("hmac", new HttpCredential("ref", "secret.acme.v1", Version: 1)),
                        TimeoutSeconds: 30)));

            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var created = await createResp.Content.ReadFromJsonAsync<ContractCreated>();

            var rotateResp = await _client.PostAsJsonAsync(
                $"/v1/contracts/{created!.Id}/credentials/rotate",
                new UpdateContractCredentialsRequest(new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("hmac", new HttpCredential("ref", "secret.acme.v2")),
                    TimeoutSeconds: 30)));

            Assert.Equal(HttpStatusCode.OK, rotateResp.StatusCode);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var row = await conn.QuerySingleAsync<(string? auth_ref, int? auth_version, string destination_config)>(
                "SELECT auth_ref, auth_version, destination_config::text FROM contracts WHERE id = @id;",
                new { id = created.Id });

            Assert.Equal("secret.acme.v2", row.auth_ref);
            Assert.Equal(2, row.auth_version);

            using var destination = JsonDocument.Parse(row.destination_config);
            var credential = destination.RootElement
                .GetProperty("Authentication")
                .GetProperty("Credential");

            Assert.Equal("secret.acme.v2", credential.GetProperty("Value").GetString());
            Assert.Equal(2, credential.GetProperty("Version").GetInt32());

            var previous = credential.GetProperty("Previous");
            Assert.Equal(1, previous.GetArrayLength());
            Assert.Equal("secret.acme.v1", previous[0].GetProperty("Value").GetString());
            Assert.Equal(1, previous[0].GetProperty("Version").GetInt32());
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
        public async Task ListByCustomer_supports_filter_sort_and_time_range()
        {
            var now = DateTimeOffset.UtcNow;
            await CreateCustomerAsync("acme");

            var createdA = await (await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.created")))
                .Content.ReadFromJsonAsync<ContractCreated>();
            var createdB = await (await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.created")))
                .Content.ReadFromJsonAsync<ContractCreated>();
            var createdC = await (await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.updated")))
                .Content.ReadFromJsonAsync<ContractCreated>();

            await _client.PostAsync($"/v1/contracts/{createdB!.Id}/disable", null);

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE contracts
                    SET created_at = CASE id
                        WHEN @IdA THEN @CreatedAtA
                        WHEN @IdB THEN @CreatedAtB
                        WHEN @IdC THEN @CreatedAtC
                        ELSE created_at
                    END
                    WHERE id IN (@IdA, @IdB, @IdC);
                    """,
                    new
                    {
                        IdA = createdA!.Id,
                        IdB = createdB.Id,
                        IdC = createdC!.Id,
                        CreatedAtA = now.AddMinutes(-25),
                        CreatedAtB = now.AddMinutes(-20),
                        CreatedAtC = now.AddMinutes(-15)
                    });
            }

            var url =
                "/v1/customers/acme/contracts?enabled=true&eventType=orders.created" +
                $"&createdFrom={Uri.EscapeDataString(now.AddMinutes(-26).ToString("O"))}" +
                $"&createdTo={Uri.EscapeDataString(now.AddMinutes(-14).ToString("O"))}" +
                "&sortBy=createdAt&sortDirection=asc&page=1&pageSize=10";

            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<PagedResponse<ContractView>>();
            Assert.NotNull(body);
            Assert.Single(body!.Items);
            Assert.Equal(createdA.Id, body.Items[0].Id);
        }

        [Fact]
        public async Task ListByCustomer_with_createdAt_sort_uses_stable_id_tiebreaker()
        {
            var now = DateTimeOffset.UtcNow;
            await CreateCustomerAsync("acme");

            var first = await (await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.created")))
                .Content.ReadFromJsonAsync<ContractCreated>();
            var second = await (await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.created")))
                .Content.ReadFromJsonAsync<ContractCreated>();

            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE contracts
                    SET created_at = @CreatedAt
                    WHERE id IN (@IdA, @IdB);
                    """,
                    new
                    {
                        IdA = first!.Id,
                        IdB = second!.Id,
                        CreatedAt = now.AddMinutes(-10)
                    });
            }

            var body = await _client.GetFromJsonAsync<PagedResponse<ContractView>>(
                "/v1/customers/acme/contracts?sortBy=createdAt&sortDirection=asc&page=1&pageSize=10");

            Assert.NotNull(body);
            var firstTwo = body!.Items.Take(2).Select(x => x.Id).ToArray();
            var expected = new[] { first.Id, second.Id }.OrderBy(x => x).ToArray();
            Assert.Equal(expected, firstTwo);
        }

        [Theory]
        [InlineData("/v1/customers/acme/contracts?sortBy=invalid")]
        [InlineData("/v1/customers/acme/contracts?sortDirection=sideways")]
        [InlineData("/v1/customers/acme/contracts?createdFrom=2026-01-02T00:00:00Z&createdTo=2026-01-01T00:00:00Z")]
        public async Task ListByCustomer_with_invalid_query_conventions_returns_400(string url)
        {
            await CreateCustomerAsync("acme");

            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task GetById_returns_etag_header()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme"));
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();

            var resp = await _client.GetAsync($"/v1/contracts/{created!.Id}");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.True(resp.Headers.ETag is not null);
        }

        [Fact]
        public async Task Put_updates_contract_configuration_atomically_with_if_match()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.created"));
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();

            var get = await _client.GetAsync($"/v1/contracts/{created!.Id}");
            var etag = get.Headers.ETag;
            Assert.NotNull(etag);

            var update = new UpdateContractRequest(
                Name: "Orders Contract v2",
                EventTypes: new[] { "orders.created", "orders.updated" },
                Configuration: new HttpDestinationConfig(
                    "https://hooks.example.com/v2",
                    new HttpAuthentication("hmac", new HttpCredential("ref", "secret.acme.v2", 2))),
                SchemaTargets: null,
                Filter: null,
                Transformation: null,
                RateLimit: new ContractRateLimitConfig(10, 60, 20),
                Ordering: new ContractOrderingConfig("order.id"));

            using var req = new HttpRequestMessage(HttpMethod.Put, $"/v1/contracts/{created.Id}")
            {
                Content = JsonContent.Create(update)
            };
            req.Headers.IfMatch.Add(etag!);

            var put = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            var body = await put.Content.ReadFromJsonAsync<ContractView>();
            Assert.NotNull(body);
            Assert.Equal("Orders Contract v2", body!.Name);
            Assert.Contains("orders.updated", body.EventTypes);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var row = await conn.QuerySingleAsync<(string? Name, string AuthRef)>(
                "SELECT name, auth_ref AS AuthRef FROM contracts WHERE id = @id;",
                new { id = created.Id });

            Assert.Equal("Orders Contract v2", row.Name);
            Assert.Equal("secret.acme.v2", row.AuthRef);
        }

        [Fact]
        public async Task Put_without_if_match_returns_400()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.created"));
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();

            var update = new UpdateContractRequest(
                Name: "n",
                EventTypes: new[] { "orders.created" },
                Configuration: new HttpDestinationConfig("https://hooks.example.com/v2", new HttpAuthentication("none")));

            var resp = await _client.PutAsJsonAsync($"/v1/contracts/{created!.Id}", update);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Put_with_stale_if_match_returns_409_conflict()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.created"));
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();

            var get = await _client.GetAsync($"/v1/contracts/{created!.Id}");
            var etag = get.Headers.ETag;
            Assert.NotNull(etag);

            await _client.PostAsync($"/v1/contracts/{created.Id}/disable", null);

            var update = new UpdateContractRequest(
                Name: "n2",
                EventTypes: new[] { "orders.created" },
                Configuration: new HttpDestinationConfig("https://hooks.example.com/v2", new HttpAuthentication("none")));

            using var req = new HttpRequestMessage(HttpMethod.Put, $"/v1/contracts/{created.Id}")
            {
                Content = JsonContent.Create(update)
            };
            req.Headers.IfMatch.Add(etag!);

            var put = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        }

        [Fact]
        public async Task Put_rejects_schema_targets_not_present_in_event_types()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.created"));
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();
            var get = await _client.GetAsync($"/v1/contracts/{created!.Id}");

            var update = new UpdateContractRequest(
                Name: "n3",
                EventTypes: new[] { "orders.created" },
                Configuration: new HttpDestinationConfig("https://hooks.example.com/v2", new HttpAuthentication("none")),
                SchemaTargets: new[]
                {
                    new ContractSchemaTargetRequest("orders.updated", 1)
                });

            using var req = new HttpRequestMessage(HttpMethod.Put, $"/v1/contracts/{created.Id}")
            {
                Content = JsonContent.Create(update)
            };
            req.Headers.IfMatch.Add(get.Headers.ETag!);

            var put = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
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

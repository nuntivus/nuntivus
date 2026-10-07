using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Controllers.Events;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Contracts.Destinations;
using NuntivusServer.Contracts.Customers;
using NuntivusServer.Contracts.Events;
using NuntivusServer.Domain.Outbox;
using Npgsql;

namespace NuntivusServer.IntegrationTests
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
        public async Task Create_with_missing_managed_secret_returns_400()
        {
            await CreateCustomerAsync("acme");
            var request = new CreateContractRequest("acme", new[] { "orders.created" },
                new NatsDestinationConfig("nats://localhost:4222", "orders.created",
                    Credentials: new NatsCredentials(Token: new DestinationCredential("ref", "secret.acme.missing"))),
                SourceIds: [ContractsApiFixture.SourceId]);

            var response = await _client.PostAsJsonAsync("/v1/contracts", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("secret.acme.missing", await response.Content.ReadAsStringAsync());
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM contracts"));
        }

        [Fact]
        public async Task Replace_and_credential_update_with_missing_secret_leave_contract_unchanged()
        {
            await CreateCustomerAsync("acme");
            var createdResponse = await _client.PostAsJsonAsync("/v1/contracts", new CreateContractRequest(
                "acme", new[] { "orders.created" },
                new HttpDestinationConfig("https://hooks.example.com/acme", new HttpAuthentication("none")),
                SourceIds: [ContractsApiFixture.SourceId]));
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            var created = await createdResponse.Content.ReadFromJsonAsync<ContractCreated>();

            var original = await _client.GetAsync($"/v1/contracts/{created!.Id}");
            var originalView = await original.Content.ReadFromJsonAsync<ContractView>();
            using var replace = new HttpRequestMessage(HttpMethod.Put, $"/v1/contracts/{created.Id}")
            {
                Content = JsonContent.Create(new UpdateContractRequest(
                    "Changed", originalView!.EventTypes,
                    new HttpDestinationConfig("https://hooks.example.com/acme",
                        new HttpAuthentication("bearer", new HttpCredential("ref", "secret.acme.missing"))),
                    SourceIds: originalView.SourceIds))
            };
            replace.Headers.IfMatch.Add(original.Headers.ETag!);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(replace)).StatusCode);

            var credentials = await _client.PostAsJsonAsync($"/v1/contracts/{created.Id}/credentials",
                new UpdateContractCredentialsRequest(new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("bearer", new HttpCredential("ref", "secret.acme.missing")))));
            Assert.Equal(HttpStatusCode.BadRequest, credentials.StatusCode);

            var after = await _client.GetFromJsonAsync<ContractView>($"/v1/contracts/{created.Id}");
            Assert.Equal(originalView.Name, after!.Name);
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var config = await conn.ExecuteScalarAsync<string>(
                "SELECT destination_config::text FROM contracts WHERE id = @Id", new { created.Id });
            Assert.DoesNotContain("secret.acme.missing", config);
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
                RateLimit: new ContractRateLimitConfig(5), SourceIds: [ContractsApiFixture.SourceId]));
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
                        new[] { new ContractPayloadFieldMappingRequest("order.id", "payload.orderId") }),
                    SourceIds: original.SourceIds))
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
                    TimeoutSeconds: 30),
                SourceIds: [ContractsApiFixture.SourceId]);

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
                "SELECT COUNT(*) FROM events WHERE source_id = @id;", new { id = ContractsApiFixture.SourceId }));

            var request = new ContractTestEventRequest(
                "orders.created", JsonSerializer.SerializeToElement(new { orderId = "matching" }),
                JsonSerializer.SerializeToElement(new { label = "smoke" }));
            var acceptedPreview = await _client.PostAsJsonAsync($"/v1/contracts/{target.Id:D}/test/preview", request);
            Assert.True((await acceptedPreview.Content.ReadFromJsonAsync<ContractTestPreviewView>())!.Matches);
            Assert.Equal(0, await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM events WHERE source_id = @id;", new { id = ContractsApiFixture.SourceId }));

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
                    }), SourceIds: [ContractsApiFixture.SourceId]);

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
                    Burst: 10), SourceIds: [ContractsApiFixture.SourceId]);

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
                Ordering: new ContractOrderingConfig("order.id"), SourceIds: [ContractsApiFixture.SourceId]);

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
                    }), SourceIds: [ContractsApiFixture.SourceId]);

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

            var definition = await _client.PostAsJsonAsync("/v1/event-definitions",
                new CreateEventDefinitionRequest("orders.created", "Order Created", null));
            Assert.Equal(HttpStatusCode.Created, definition.StatusCode);

            var draft = await _client.PostAsJsonAsync(
                "/v1/event-definitions/orders.created/schemas",
                new CreateEventSchemaVersionRequest(1, "{}", SchemaCompatibilityMode.Backward));
            Assert.Equal(HttpStatusCode.Created, draft.StatusCode);

            var req = new CreateContractRequest(
                "acme",
                new[] { "orders.created" },
                new HttpDestinationConfig(
                    "https://hooks.example.com/acme",
                    new HttpAuthentication("none")),
                new[] { new ContractSchemaTargetRequest("orders.created", 1) }, SourceIds: [ContractsApiFixture.SourceId]);

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
                new[] { new ContractSchemaTargetRequest("orders.created", 99) }, SourceIds: [ContractsApiFixture.SourceId]);

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
        public async Task Contract_detail_remains_available_across_repeated_requests()
        {
            await CreateCustomerAsync("acme");
            var created = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var contract = await created.Content.ReadFromJsonAsync<ContractCreated>();

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var response = await _client.GetAsync($"/v1/contracts/{contract!.Id}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(contract.Id, (await response.Content.ReadFromJsonAsync<ContractView>())!.Id);
            }
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
                    Headers: new Dictionary<string, string> { ["X-App"] = "nuntivus" },
                    SecretHeaders: new Dictionary<string, string> { ["X-Api-Key"] = "abc123" },
                    TimeoutSeconds: 30), SourceIds: [ContractsApiFixture.SourceId]);

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
                        TimeoutSeconds: 30), SourceIds: [ContractsApiFixture.SourceId]));

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
                Ordering: new ContractOrderingConfig("order.id"),
                SourceIds: [ContractsApiFixture.SourceId]);

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
        public async Task Put_response_etag_can_be_used_for_next_update()
        {
            await CreateCustomerAsync("acme");
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme", "orders.created"));
            var created = await create.Content.ReadFromJsonAsync<ContractCreated>();
            var url = $"/v1/contracts/{created!.Id}";

            var get = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);

            using var firstRequest = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = JsonContent.Create(new UpdateContractRequest(
                    "First update", new[] { "orders.created" },
                    new HttpDestinationConfig("https://hooks.example.com/v2", new HttpAuthentication("none")),
                    SourceIds: [ContractsApiFixture.SourceId]))
            };
            firstRequest.Headers.IfMatch.Add(get.Headers.ETag!);

            var first = await _client.SendAsync(firstRequest);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.NotNull(first.Headers.ETag);

            var persisted = await _client.GetAsync(url);
            Assert.Equal(first.Headers.ETag, persisted.Headers.ETag);

            using var secondRequest = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = JsonContent.Create(new UpdateContractRequest(
                    "Second update", new[] { "orders.created" },
                    new HttpDestinationConfig("https://hooks.example.com/v3", new HttpAuthentication("none")),
                    SourceIds: [ContractsApiFixture.SourceId]))
            };
            secondRequest.Headers.IfMatch.Add(first.Headers.ETag!);

            var second = await _client.SendAsync(secondRequest);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var updated = await second.Content.ReadFromJsonAsync<ContractView>();
            Assert.Equal("Second update", updated!.Name);
        }

        [Fact]
        public async Task Draft_destination_cannot_be_linked_until_configured_and_enabled()
        {
            await CreateCustomerAsync("acme");
            var created = await _client.PostAsJsonAsync("/v1/destinations", new CreateDestinationRequest(
                "Draft hook", "Created without settings", null, DestinationType.Http));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var draft = await created.Content.ReadFromJsonAsync<DestinationResourceView>();
            Assert.NotNull(draft);
            Assert.False(draft.Configured);
            Assert.False(draft.Enabled);
            Assert.Equal("{}", draft.Configuration);

            var link = ValidRequest("acme") with { Configuration = null, DestinationIds = [draft.Id] };
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/v1/contracts", link)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/v1/destinations/{draft.Id}/enable", null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/v1/destinations/{draft.Id}",
                new UpdateDestinationRequest(draft.Name, draft.Description,
                    new HttpDestinationConfig("", new HttpAuthentication("none"))))).StatusCode);

            var configured = await _client.PutAsJsonAsync($"/v1/destinations/{draft.Id}",
                new UpdateDestinationRequest(draft.Name, draft.Description,
                    new HttpDestinationConfig("https://hooks.example.com/draft", new HttpAuthentication("none"))));
            Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
            var ready = await configured.Content.ReadFromJsonAsync<DestinationResourceView>();
            Assert.True(ready!.Configured);
            Assert.False(ready.Enabled);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/v1/contracts", link)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/v1/destinations/{draft.Id}/enable", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/v1/contracts", link)).StatusCode);

        }

        [Fact]
        public async Task Shared_destination_edit_does_not_change_contract_snapshots_until_refreshed()
        {
            await CreateCustomerAsync("acme");
            await CreateCustomerAsync("beta");
            var createDestination = await _client.PostAsJsonAsync("/v1/destinations", new CreateDestinationRequest(
                "Shared hook", null, new HttpDestinationConfig("https://hooks.example.com/one", new HttpAuthentication("none"))));
            Assert.Equal(HttpStatusCode.Created, createDestination.StatusCode);
            var destination = await createDestination.Content.ReadFromJsonAsync<DestinationResourceView>();

            var first = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme") with
            { Configuration = null, DestinationIds = [destination!.Id] });
            var second = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("beta") with
            { Configuration = null, DestinationIds = [destination.Id] });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            var firstContract = await first.Content.ReadFromJsonAsync<ContractCreated>();
            var secondContract = await second.Content.ReadFromJsonAsync<ContractCreated>();

            var update = await _client.PutAsJsonAsync($"/v1/destinations/{destination.Id}",
                new UpdateDestinationRequest("Shared hook", null,
                    new HttpDestinationConfig("https://hooks.example.com/two", new HttpAuthentication("none"))));
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            var firstGet = await _client.GetAsync($"/v1/contracts/{firstContract!.Id}");
            var firstSnapshot = await firstGet.Content.ReadFromJsonAsync<ContractView>();
            var secondSnapshot = await _client.GetFromJsonAsync<ContractView>($"/v1/contracts/{secondContract!.Id}");
            Assert.Contains("/one", firstSnapshot!.Destinations!.Single().Configuration);
            Assert.Contains("/one", secondSnapshot!.Destinations!.Single().Configuration);

            using var refresh = new HttpRequestMessage(HttpMethod.Put, $"/v1/contracts/{firstContract.Id}")
            {
                Content = JsonContent.Create(new UpdateContractRequest(null, ["orders.created"], null,
                    SourceIds: [ContractsApiFixture.SourceId], DestinationIds: [destination.Id],
                    RefreshDestinationIds: [destination.Id]))
            };
            refresh.Headers.IfMatch.Add(firstGet.Headers.ETag!);
            var refreshedResponse = await _client.SendAsync(refresh);
            Assert.Equal(HttpStatusCode.OK, refreshedResponse.StatusCode);
            var refreshed = await refreshedResponse.Content.ReadFromJsonAsync<ContractView>();
            Assert.Contains("/two", refreshed!.Destinations!.Single().Configuration);
            Assert.Contains("/one", (await _client.GetFromJsonAsync<ContractView>($"/v1/contracts/{secondContract.Id}"))!
                .Destinations!.Single().Configuration);
        }

        [Fact]
        public async Task Destination_disable_and_delete_choices_keep_or_remove_bindings()
        {
            await CreateCustomerAsync("acme");
            var createdDestination = await _client.PostAsJsonAsync("/v1/destinations", new CreateDestinationRequest(
                "First hook", null, new HttpDestinationConfig("https://hooks.example.com/one", new HttpAuthentication("none"))));
            var first = await createdDestination.Content.ReadFromJsonAsync<DestinationResourceView>();
            var secondResponse = await _client.PostAsJsonAsync("/v1/destinations", new CreateDestinationRequest(
                "Second hook", null, new HttpDestinationConfig("https://hooks.example.com/two", new HttpAuthentication("none"))));
            var second = await secondResponse.Content.ReadFromJsonAsync<DestinationResourceView>();
            var created = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme") with
            { Configuration = null, DestinationIds = [first!.Id, second!.Id] });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var contract = await created.Content.ReadFromJsonAsync<ContractCreated>();

            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/v1/destinations/{first.Id}/disable",
                new DisableDestinationRequest(false))).StatusCode);
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            Assert.True(await conn.ExecuteScalarAsync<bool>(
                "SELECT delivery_enabled FROM contract_destinations WHERE destination_id = @Id", new { first.Id }));
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/v1/destinations/{first.Id}/disable",
                new DisableDestinationRequest(true))).StatusCode);
            Assert.False(await conn.ExecuteScalarAsync<bool>(
                "SELECT delivery_enabled FROM contract_destinations WHERE destination_id = @Id", new { first.Id }));
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/v1/destinations/{first.Id}/enable", null)).StatusCode);
            Assert.True(await conn.ExecuteScalarAsync<bool>(
                "SELECT delivery_enabled FROM contract_destinations WHERE destination_id = @Id", new { first.Id }));

            Assert.Equal(HttpStatusCode.NoContent,
                (await _client.DeleteAsync($"/v1/destinations/{first.Id}?removeBindings=false")).StatusCode);
            var detachedResponse = await _client.GetAsync($"/v1/contracts/{contract!.Id}");
            var detached = await detachedResponse.Content.ReadFromJsonAsync<ContractView>();
            Assert.Contains(detached!.Destinations!, binding => binding.DestinationId is null);
            using var edit = new HttpRequestMessage(HttpMethod.Put, $"/v1/contracts/{contract.Id}")
            {
                Content = JsonContent.Create(new UpdateContractRequest("Edited", ["orders.created"], null,
                    SourceIds: [ContractsApiFixture.SourceId], DestinationIds: [second.Id]))
            };
            edit.Headers.IfMatch.Add(detachedResponse.Headers.ETag!);
            var edited = await _client.SendAsync(edit);
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            Assert.Equal(2, (await edited.Content.ReadFromJsonAsync<ContractView>())!.Destinations!.Count);
            Assert.Equal(HttpStatusCode.NoContent,
                (await _client.DeleteAsync($"/v1/destinations/{second.Id}?removeBindings=true")).StatusCode);
            var remaining = await _client.GetFromJsonAsync<ContractView>($"/v1/contracts/{contract.Id}");
            Assert.Single(remaining!.Destinations!);
            Assert.Null(remaining.Destinations.Single().DestinationId);
        }

        [Fact]
        public async Task Test_event_send_fans_out_to_all_selected_destinations()
        {
            await CreateCustomerAsync("acme");
            var one = await _client.PostAsJsonAsync("/v1/destinations", new CreateDestinationRequest(
                "One", null, new HttpDestinationConfig("https://hooks.example.com/one", new HttpAuthentication("none"))));
            var two = await _client.PostAsJsonAsync("/v1/destinations", new CreateDestinationRequest(
                "Two", null, new HttpDestinationConfig("https://hooks.example.com/two", new HttpAuthentication("none"))));
            var first = await one.Content.ReadFromJsonAsync<DestinationResourceView>();
            var second = await two.Content.ReadFromJsonAsync<DestinationResourceView>();
            var create = await _client.PostAsJsonAsync("/v1/contracts", ValidRequest("acme") with
            { Configuration = null, DestinationIds = [first!.Id, second!.Id] });
            var contract = await create.Content.ReadFromJsonAsync<ContractCreated>();

            var send = await _client.PostAsJsonAsync($"/v1/contracts/{contract!.Id}/test/send",
                new ContractTestEventRequest("orders.created", JsonSerializer.SerializeToElement(new { orderId = "sample" }), null));
            Assert.Equal(HttpStatusCode.OK, send.StatusCode);
            var result = await send.Content.ReadFromJsonAsync<ContractTestSendView>();
            Assert.Equal(2, result!.DeliveryIds!.Count);
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            Assert.Equal(2, await conn.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM deliveries WHERE event_id = @Id AND contract_id = @ContractId",
                new { Id = result.EventId, ContractId = contract.Id }));
        }

        [Fact]
        public async Task Destination_management_redacts_credentials_and_requires_operator_for_writes()
        {
            var created = await _client.PostAsJsonAsync("/v1/destinations", new CreateDestinationRequest(
                "Secure hook", null, new HttpDestinationConfig("https://hooks.example.com/secure",
                    new HttpAuthentication("bearer", new HttpCredential("ref", "secret.acme.webhook")))));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var destination = await created.Content.ReadFromJsonAsync<DestinationResourceView>();
            Assert.Contains("***", destination!.Configuration);
            Assert.DoesNotContain("secret.acme.webhook", destination.Configuration);

            var updated = await _client.PutAsJsonAsync($"/v1/destinations/{destination.Id}",
                new UpdateDestinationRequest("Secure hook", null,
                    new HttpDestinationConfig("https://hooks.example.com/updated",
                        new HttpAuthentication("bearer", new HttpCredential("ref", "***")))));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            var configuration = await conn.ExecuteScalarAsync<string>(
                "SELECT configuration::text FROM destinations WHERE id = @Id", new { destination.Id });
            Assert.Contains("secret.acme.webhook", configuration);
            Assert.DoesNotContain("***", configuration);

            using var viewer = _fixture.CreateClient();
            viewer.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            viewer.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/v1/destinations/{destination.Id}")).StatusCode);
            var denied = await viewer.PostAsJsonAsync("/v1/destinations", new CreateDestinationRequest(
                "Denied", null, new HttpDestinationConfig("https://hooks.example.com/denied", new HttpAuthentication("none"))));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
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
                Configuration: new HttpDestinationConfig("https://hooks.example.com/v2", new HttpAuthentication("none")),
                SourceIds: [ContractsApiFixture.SourceId]);

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

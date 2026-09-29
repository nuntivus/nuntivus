using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Controllers.Events;
using NuntivusServer.Contracts.Sources;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(EventsApiCollection))]
    public sealed class SourcesApiTests : IAsyncLifetime
    {
        private readonly EventsApiFixture _fixture;
        private readonly HttpClient _client;

        public SourcesApiTests(EventsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

        private async Task<SourceView> CreateAsync()
        {
            var response = await _client.PostAsJsonAsync("/v1/sources", new
            {
                name = "Orders", type = "HTTP_API", configuration = new { authentication = new { type = "none" } }
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<SourceView>())!;
        }

        [Fact]
        public async Task ManagesSourceAndRejectsIngestionWhenDisabled()
        {
            var source = await CreateAsync();
            Assert.True(source.Enabled);
            var get = await _client.GetFromJsonAsync<SourceView>($"/v1/sources/{source.Id}");
            Assert.Equal(source.Id, get!.Id);
            var list = await _client.GetFromJsonAsync<PagedResponse<SourceView>>("/v1/sources?page=1&pageSize=5");
            Assert.Equal(1, list!.TotalCount);
            await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
            {
                var actor = await connection.ExecuteScalarAsync<string>(
                    "SELECT actor_id FROM audit_logs WHERE category = 'source' AND action = 'created' LIMIT 1");
                Assert.Equal("operator-it", actor);
            }

            var update = await _client.PutAsJsonAsync($"/v1/sources/{source.Id}", new
            {
                name = "New Orders", configuration = new { authentication = new { type = "none" } }
            });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            var disable = await _client.PostAsync($"/v1/sources/{source.Id}/disable", null);
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
            var rejected = await _client.PostAsJsonAsync($"/v1/sources/{source.Id}/events", EventRequest());
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            var enable = await _client.PostAsync($"/v1/sources/{source.Id}/enable", null);
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
            var delete = await _client.DeleteAsync($"/v1/sources/{source.Id}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        [Fact]
        public async Task InvalidAndNonOperationalTypesAreRejected()
        {
            foreach (var type in new[] { "NOT_REAL", "KAFKA", "SQS", "HTTP_POLLING", "FOLDER" })
            {
                var response = await _client.PostAsJsonAsync("/v1/sources", new
                {
                    name = "invalid", type, configuration = new { authentication = new { type = "none" } }
                });
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            var invalid = await _client.PostAsJsonAsync("/v1/sources", new
            {
                name = "inline", type = "HTTP_API", configuration = new { authentication = new { type = "token", value = "private" } }
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.DoesNotContain("private", await invalid.Content.ReadAsStringAsync());

            var missingSecret = await _client.PostAsJsonAsync("/v1/sources", new
            {
                name = "missing secret", type = "HTTP_API",
                configuration = new { authentication = new { type = "token", secretRef = "secret.missing.source" } }
            });
            Assert.Equal(HttpStatusCode.BadRequest, missingSecret.StatusCode);
        }

        [Fact]
        public async Task IngestionRejectsUnknownAndWrongType()
        {
            var unknown = await _client.PostAsJsonAsync($"/v1/sources/{Guid.NewGuid():D}/events", EventRequest());
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

            var id = Guid.NewGuid();
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.ExecuteAsync("""
                INSERT INTO sources (id, name, type, enabled, configuration, created_at, updated_at)
                VALUES (@Id, 'not-http', 'KAFKA', TRUE, '{}'::jsonb, now(), now())
                """, new { Id = id });
            var wrongType = await _client.PostAsJsonAsync($"/v1/sources/{id:D}/events", EventRequest());
            Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        }

        [Fact]
        public async Task HttpIngestionCreatesCanonicalEventAndOutboxIdempotently()
        {
            var source = await CreateAsync();
            var first = await _client.PostAsJsonAsync($"/v1/sources/{source.Id}/events", EventRequest());
            var second = await _client.PostAsJsonAsync($"/v1/sources/{source.Id}/events", EventRequest());
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var ack = (await first.Content.ReadFromJsonAsync<PublishEventResponse>())!;
            Assert.Equal(ack.Id, (await second.Content.ReadFromJsonAsync<PublishEventResponse>())!.Id);

            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            var persistedSource = await connection.ExecuteScalarAsync<Guid>(
                "SELECT source_id FROM events WHERE id = @Id", new { Id = Guid.Parse(ack.Id) });
            var metadata = await connection.ExecuteScalarAsync<string>(
                "SELECT metadata::text FROM events WHERE id = @Id", new { Id = Guid.Parse(ack.Id) });
            var payload = await connection.ExecuteScalarAsync<string>(
                "SELECT payload::text FROM events WHERE id = @Id", new { Id = Guid.Parse(ack.Id) });
            Assert.Equal(Guid.Parse(source.Id), persistedSource);
            Assert.Contains("traceId", metadata);
            Assert.Contains("o-1", payload);
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM outbox_messages WHERE event_id = @Id", new { Id = Guid.Parse(ack.Id) }));
            Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/v1/sources/{source.Id}")).StatusCode);
        }

        [Fact]
        public async Task SensitiveMetadataIsRejectedBeforePersistence()
        {
            var source = await CreateAsync();
            var response = await _client.PostAsJsonAsync($"/v1/sources/{source.Id}/events", new
            {
                idempotencyKey = "metadata-secret", type = "orders.created", payload = new { id = 1 },
                metadata = new { authorization = "Bearer private" }
            });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain("private", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task TokenAuthenticationUsesManagedSecretWithoutExposingIt()
        {
            var secretName = "secret.source.orders";
            var secret = await _client.PostAsJsonAsync("/v1/admin/secrets", new
            {
                name = secretName, value = "integration-source-token"
            });
            Assert.Equal(HttpStatusCode.Created, secret.StatusCode);

            var created = await _client.PostAsJsonAsync("/v1/sources", new
            {
                name = "Authenticated", type = "HTTP_API",
                configuration = new { authentication = new { type = "token", secretRef = secretName } }
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var responseBody = await created.Content.ReadAsStringAsync();
            Assert.DoesNotContain("integration-source-token", responseBody);
            var source = (await created.Content.ReadFromJsonAsync<SourceView>())!;

            var unauthorized = await _client.PostAsJsonAsync($"/v1/sources/{source.Id}/events", EventRequest());
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/sources/{source.Id}/events");
            request.Headers.Add("X-Nuntivus-Source-Token", "integration-source-token");
            request.Content = JsonContent.Create(EventRequest());
            var accepted = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var ack = (await accepted.Content.ReadFromJsonAsync<PublishEventResponse>())!;
            await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
            {
                var metadata = await connection.ExecuteScalarAsync<string>(
                    "SELECT metadata::text FROM events WHERE id = @Id", new { Id = Guid.Parse(ack.Id) });
                Assert.DoesNotContain("integration-source-token", metadata);
            }

            var protectedSecret = await _client.DeleteAsync($"/v1/admin/secrets/{secretName}");
            Assert.Equal(HttpStatusCode.Conflict, protectedSecret.StatusCode);
        }

        private static object EventRequest() => new
        {
            idempotencyKey = "idem-orders", type = "orders.created", timestamp = DateTimeOffset.UtcNow,
            payload = new { orderId = "o-1" }, metadata = new { traceId = "trace-1" }
        };
    }
}

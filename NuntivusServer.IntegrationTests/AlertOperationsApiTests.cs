using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Alerts;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Contracts.Destinations;
using NuntivusServer.Infastructure.Persistence.Postgres;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(ContractsApiCollection))]
    public sealed class AlertOperationsApiTests(ContractsApiFixture fixture) : IAsyncLifetime
    {
        private HttpClient _client = null!;
        public async Task InitializeAsync()
        {
            await fixture.ResetAsync();
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
        }
        public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }
        private async Task<(Guid Rule, Guid Notice, Guid Contract)> SeedAsync()
        {
            await _client.PostAsJsonAsync("/v1/customers", new { id = "ops-customer", name = "Operations" });
            var created = await _client.PostAsJsonAsync("/v1/contracts", new CreateContractRequest("ops-customer", ["orders.created"],
                new HttpDestinationConfig("https://hooks.example.com/events", new HttpAuthentication("none")), SourceIds: [ContractsApiFixture.SourceId]));
            var contract = (await created.Content.ReadFromJsonAsync<ContractCreated>())!.Id;
            var rule = Guid.NewGuid(); var notice = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{rule}", new AlertRuleRequest(contract, "Operations", "DeadLetters", 1))).StatusCode);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("""
                INSERT INTO alert_notifications (id, rule_id, contract_id, state, message, created_at) VALUES (@Notice, @Rule, @Contract, 'Firing', 'Operations', now());
                INSERT INTO alert_webhook_outbox (notification_id, endpoint, payload, next_attempt_at) VALUES (@Notice, 'https://hooks.example.com/private', '{}', now());
                """, new { Notice = notice, Rule = rule, Contract = contract });
            return (rule, notice, contract);
        }
        private Task<AlertWebhookStatusView?> Status(Guid notice) => _client.GetFromJsonAsync<AlertWebhookStatusView>($"/v1/alerts/notifications/{notice}/webhook");
        private sealed record AttemptPage(AlertWebhookAttemptView[] Items);
        [Fact]
        public async Task Archive_is_idempotent_retains_history_and_queued_sends_and_prevents_reenable()
        {
            var (rule, notice, contract) = await SeedAsync();
            var response = await _client.DeleteAsync($"/v1/alerts/rules/{rule}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True((await response.Content.ReadFromJsonAsync<AlertRuleView>())!.Archived);
            Assert.Equal(HttpStatusCode.OK, (await _client.DeleteAsync($"/v1/alerts/rules/{rule}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{rule}", new AlertRuleRequest(contract, "Operations", "DeadLetters", 1))).StatusCode);
            Assert.Equal("Pending", (await Status(notice))!.Status);
            var queue = new PostgresAlertWebhookQueue(new PostgresDatabaseConnectionFactory(fixture.ConnectionString));
            Assert.NotNull((await queue.ClaimAsync(CancellationToken.None)).EnsureValue());
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE action = 'rule_archived'"));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM alert_notifications WHERE id = @Id", new { Id = notice }));
            using var scope = fixture.Services.CreateScope();
            Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<IAlertService>().EvaluateAsync(CancellationToken.None)).EnsureValue());
        }
        [Fact]
        public async Task Failed_webhook_retry_preserves_history_and_idempotency_and_rejects_active_or_successful_sends()
        {
            var (_, notice, _) = await SeedAsync();
            var path = $"/v1/alerts/notifications/{notice}/webhook";
            var queue = new PostgresAlertWebhookQueue(new PostgresDatabaseConnectionFactory(fixture.ConnectionString));
            var first = (await queue.ClaimAsync(CancellationToken.None)).EnsureValue()!;
            Assert.Equal("Sending", (await Status(notice))!.Status);
            Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync($"{path}/retry", new AlertWebhookRetryRequest(Guid.NewGuid()))).StatusCode);
            (await queue.CompleteAsync(first, false, CancellationToken.None)).EnsureSuccess();
            var failed = (await Status(notice))!;
            Assert.Equal("Failed", failed.Status); Assert.True(failed.CanRetry);
            var request = new AlertWebhookRetryRequest(Guid.NewGuid());
            var retries = await Task.WhenAll(_client.PostAsJsonAsync($"{path}/retry", request), _client.PostAsJsonAsync($"{path}/retry", request));
            Assert.All(retries, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(1, (await Status(notice))!.AttemptCount);
            var second = (await queue.ClaimAsync(CancellationToken.None)).EnsureValue()!;
            Assert.Equal(2, second.AttemptCount);
            (await queue.CompleteAsync(first, true, CancellationToken.None)).EnsureSuccess();
            Assert.Equal("Sending", (await Status(notice))!.Status);
            (await queue.CompleteAsync(second, true, CancellationToken.None)).EnsureSuccess();
            Assert.Equal("Succeeded", (await Status(notice))!.Status);
            Assert.False((await Status(notice))!.CanRetry);
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"{path}/retry", request)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync($"{path}/retry", new AlertWebhookRetryRequest(Guid.NewGuid()))).StatusCode);
            var history = await _client.GetFromJsonAsync<AttemptPage>($"{path}/attempts");
            Assert.Equal(new[] { "Succeeded", "Failed" }, history!.Items.Select(attempt => attempt.Outcome));
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE action = 'webhook_retried'"));
            var json = await _client.GetStringAsync(path);
            Assert.DoesNotContain("hooks.example.com", json); Assert.DoesNotContain("secret", json);
        }
        [Fact]
        public async Task Expired_exhausted_attempt_can_be_retried_with_fresh_budget_and_safe_history()
        {
            var (_, notice, _) = await SeedAsync();
            var queue = new PostgresAlertWebhookQueue(new PostgresDatabaseConnectionFactory(fixture.ConnectionString));
            var first = (await queue.ClaimAsync(CancellationToken.None)).EnsureValue()!;
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("UPDATE alert_webhook_outbox SET attempt_count = 10, leased_until = now() - interval '1 second' WHERE notification_id = @Id", new { Id = notice });
            Assert.Null((await queue.ClaimAsync(CancellationToken.None)).EnsureValue());
            Assert.Equal("Exhausted", (await Status(notice))!.Status);
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/v1/alerts/notifications/{notice}/webhook/retry", new AlertWebhookRetryRequest(Guid.NewGuid()))).StatusCode);
            var next = (await queue.ClaimAsync(CancellationToken.None)).EnsureValue()!;
            Assert.Equal(11, next.AttemptCount);
            Assert.Equal(1, (await Status(notice))!.CycleAttemptCount);
            (await queue.CompleteAsync(first, true, CancellationToken.None)).EnsureSuccess();
            Assert.Equal("Sending", (await Status(notice))!.Status);
            Assert.Equal("LeaseExpired", await connection.ExecuteScalarAsync<string>("SELECT outcome FROM alert_webhook_attempts WHERE lease_token = @Token", new { Token = first.LeaseToken }));
            for (var index = 1; index <= 10; index++)
            {
                (await queue.CompleteAsync(next, false, CancellationToken.None)).EnsureSuccess();
                if (index < 10)
                {
                    await connection.ExecuteAsync("UPDATE alert_webhook_outbox SET next_attempt_at = now() WHERE notification_id = @Id", new { Id = notice });
                    next = (await queue.ClaimAsync(CancellationToken.None)).EnsureValue()!;
                }
            }
            Assert.Equal("Exhausted", (await Status(notice))!.Status);
            Assert.Equal(20, (await Status(notice))!.AttemptCount);
            Assert.Equal(10, (await Status(notice))!.CycleAttemptCount);
            Assert.Null((await queue.ClaimAsync(CancellationToken.None)).EnsureValue());
        }
        [Fact]
        public async Task Viewer_can_inspect_but_cannot_archive_or_retry()
        {
            var (rule, notice, _) = await SeedAsync();
            _client.DefaultRequestHeaders.Remove("X-Test-Role"); _client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/v1/alerts/notifications/{notice}/webhook")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/v1/alerts/notifications/{notice}/webhook/attempts")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.DeleteAsync($"/v1/alerts/rules/{rule}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync($"/v1/alerts/notifications/{notice}/webhook/retry", new AlertWebhookRetryRequest(Guid.NewGuid()))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/v1/alerts/notifications/{Guid.NewGuid()}/webhook")).StatusCode);
        }
    }
}

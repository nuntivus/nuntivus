using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Contracts.Alerts;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Contracts.Destinations;
using NuntivusServer.Infastructure.Persistence.Postgres;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(ContractsApiCollection))]
    public sealed class AlertApiTests(ContractsApiFixture fixture) : IAsyncLifetime
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

        [Fact]
        public async Task Healthy_binding_does_not_mask_another_bindings_failures()
        {
            var contract = await CreateContract();
            var ruleId = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{ruleId}",
                new AlertRuleRequest(contract, "Binding failures", "DestinationFailures", 3, 0))).StatusCode);
            var factory = new PostgresDatabaseConnectionFactory(fixture.ConnectionString);
            var repository = new PostgresDeliveryRepository(factory);
            var failing = Guid.NewGuid();
            var healthy = Guid.NewGuid();
            async Task Observe(Guid binding, bool succeeded)
            {
                using var uow = new PostgresUnitOfWork(factory);
                uow.Begin(true);
                (await repository.UpsertDestinationHealthAsync(new NuntivusServer.Application.Deliveries.Health.DestinationHealthObservation(
                    contract, "alert-customer", "Http", succeeded, DateTimeOffset.UtcNow, 10, binding), uow, CancellationToken.None)).EnsureSuccess();
                uow.Commit();
            }
            for (var index = 0; index < 3; index++) { await Observe(failing, false); await Observe(healthy, true); }
            using var scope = fixture.Services.CreateScope();
            var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();
            Assert.Equal(1, (await alerts.EvaluateAsync(CancellationToken.None)).EnsureValue());
            await Observe(healthy, true);
            Assert.Equal(0, (await alerts.EvaluateAsync(CancellationToken.None)).EnsureValue());
            await Observe(failing, true);
            Assert.Equal(1, (await alerts.EvaluateAsync(CancellationToken.None)).EnsureValue());
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            Assert.Equal(new[] { "Firing", "Recovered" }, (await connection.QueryAsync<string>(
                "SELECT state FROM alert_notifications WHERE rule_id = @Id ORDER BY created_at", new { Id = ruleId })).ToArray());
        }

        [Fact]
        public async Task Rules_emit_deduplicated_firing_reminder_recovery_and_durable_webhook()
        {
            var contract = await CreateContract();
            var ruleId = Guid.NewGuid();
            var rule = new AlertRuleRequest(contract, "Dead deliveries", "DeadLetters", 1, 60, WebhookUrl: "https://hooks.example.com/alerts");
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{ruleId}", rule)).StatusCode);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            var delivery = Guid.NewGuid();
            await connection.ExecuteAsync("""
                INSERT INTO events (id, type, idempotency_key, source_id, occurred_at, received_at, payload)
                VALUES (@Id, 'orders.created', @Key, @Source, now(), now(), '{}');
                INSERT INTO deliveries (id, event_id, contract_id, destination_binding_id, customer_id, event_type, payload,
                    destination_type, destination_config, status, attempt_count, created_at, updated_at)
                SELECT @Id, @Id, c.id, b.id, c.customer_id, 'orders.created', '{}', 'Http', c.destination_config, 'Dead', 0, now(), now()
                FROM contracts c JOIN contract_destinations b ON b.contract_id = c.id WHERE c.id = @Contract LIMIT 1;
                """, new { Id = delivery, Key = delivery.ToString(), Source = ContractsApiFixture.SourceId, Contract = contract });
            using var scope = fixture.Services.CreateScope();
            var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();
            Assert.Equal(1, (await alerts.EvaluateAsync(CancellationToken.None)).EnsureValue());
            Assert.Equal(0, (await alerts.EvaluateAsync(CancellationToken.None)).EnsureValue());
            var exported = await _client.GetStringAsync($"/v1/exports/deliveries?contractId={contract}&status=Dead");
            Assert.Contains(delivery.ToString(), exported);
            Assert.DoesNotContain("destinationConfig", exported);
            Assert.DoesNotContain("hooks.example.com", exported);
            await connection.ExecuteAsync("UPDATE alert_rules SET last_notified_at = now() - interval '61 seconds' WHERE id = @Id", new { Id = ruleId });
            Assert.Equal(1, (await alerts.EvaluateAsync(CancellationToken.None)).EnsureValue());
            await connection.ExecuteAsync("UPDATE deliveries SET status = 'Delivered' WHERE id = @Id", new { Id = delivery });
            Assert.Equal(1, (await alerts.EvaluateAsync(CancellationToken.None)).EnsureValue());
            Assert.Equal(new[] { "Firing", "Reminder", "Recovered" }, (await connection.QueryAsync<string>("SELECT state FROM alert_notifications WHERE rule_id = @Id ORDER BY created_at", new { Id = ruleId })).ToArray());
            var notificationId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM alert_notifications WHERE rule_id = @Id AND state = 'Firing'", new { Id = ruleId });
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/v1/alerts/notifications/{notificationId}/acknowledge", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/v1/alerts/notifications/{notificationId}/acknowledge", new { })).StatusCode);
            var queue = new PostgresAlertWebhookQueue(new PostgresDatabaseConnectionFactory(fixture.ConnectionString));
            var message = (await queue.ClaimAsync(CancellationToken.None)).EnsureValue()!;
            Assert.Equal(1, message.AttemptCount);
            (await queue.CompleteAsync(message, false, CancellationToken.None)).EnsureSuccess();
            Assert.Null(await connection.ExecuteScalarAsync<DateTimeOffset?>("SELECT completed_at FROM alert_webhook_outbox WHERE notification_id = @Id", new { Id = message.NotificationId }));
            Assert.True(await connection.ExecuteScalarAsync<bool>("SELECT next_attempt_at > now() FROM alert_webhook_outbox WHERE notification_id = @Id", new { Id = message.NotificationId }));
        }

        [Fact]
        public async Task Unsafe_webhooks_and_unauthorized_rule_changes_are_rejected()
        {
            var contract = await CreateContract();
            var rule = new AlertRuleRequest(contract, "Failures", "DestinationFailures", 3, WebhookUrl: "https://127.0.0.1/alerts");
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{Guid.NewGuid()}", rule)).StatusCode);
            _client.DefaultRequestHeaders.Remove("X-Test-Role");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{Guid.NewGuid()}", rule with { WebhookUrl = null })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/v1/alerts/notifications")).StatusCode);
        }
        [Fact]
        public async Task Evaluator_rotates_bounded_batches_and_deduplicates_concurrent_evaluation()
        {
            var contract = await CreateContract();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("""
                INSERT INTO alert_rules (id, contract_id, name, signal, threshold, reminder_seconds, enabled, updated_at)
                SELECT gen_random_uuid(), @Contract, 'Failure rule', 'DestinationFailures', 1, 0, true, now() FROM generate_series(1, 101);
                INSERT INTO destination_health (contract_id, customer_id, destination_type, consecutive_failures, last_attempt_at,
                    health_status, created_at, updated_at) VALUES (@Contract, 'alert-customer', 'Http', 3, now(), 'Unhealthy', now(), now());
                """, new { Contract = contract });
            using var first = fixture.Services.CreateScope();
            using var second = fixture.Services.CreateScope();
            var service = first.ServiceProvider.GetRequiredService<IAlertService>();
            Assert.Equal(100, (await service.EvaluateAsync(CancellationToken.None)).EnsureValue());
            Assert.Equal(1, (await service.EvaluateAsync(CancellationToken.None)).EnsureValue());
            var results = await Task.WhenAll(service.EvaluateAsync(CancellationToken.None), second.ServiceProvider.GetRequiredService<IAlertService>().EvaluateAsync(CancellationToken.None));
            Assert.All(results, result => Assert.Equal(0, result.EnsureValue()));
            Assert.Equal(101, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM alert_notifications"));
        }

        [Fact]
        public async Task Slo_no_data_does_not_fire_and_expired_budget_fires_once()
        {
            var contract = await CreateContract();
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/v1/contracts/{contract}/slo", new ContractSloPolicyRequest(99, 95, 60))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{Guid.NewGuid()}", new AlertRuleRequest(contract, "Success budget", "SloSuccess", 0))).StatusCode);
            using var scope = fixture.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IAlertService>();
            Assert.Equal(0, (await service.EvaluateAsync(CancellationToken.None)).EnsureValue());
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("""
                UPDATE contract_slo_policies SET monitoring_started_at = now() - interval '3 days' WHERE contract_id = @Contract;
                INSERT INTO delivery_slo_observations (delivery_id, contract_id, customer_id, created_at, excluded)
                VALUES (@Id, @Contract, 'alert-customer', now() - interval '2 days', false);
                """, new { Id = Guid.NewGuid(), Contract = contract });
            Assert.Equal(1, (await service.EvaluateAsync(CancellationToken.None)).EnsureValue());
            Assert.Equal(0, (await service.EvaluateAsync(CancellationToken.None)).EnsureValue());
        }

        [Fact]
        public async Task Queue_reclaims_expired_lease_ignores_stale_completion_and_caps_attempts()
        {
            var contract = await CreateContract();
            var rule = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{rule}", new AlertRuleRequest(contract, "Queue", "DeadLetters", 1))).StatusCode);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            var id = Guid.NewGuid();
            await connection.ExecuteAsync("""
                INSERT INTO alert_notifications (id, rule_id, contract_id, state, message, created_at) VALUES (@Id, @Rule, @Contract, 'Firing', 'Queue test', now());
                INSERT INTO alert_webhook_outbox (notification_id, endpoint, payload, next_attempt_at) VALUES (@Id, 'https://hooks.example.com/alert', '{}', now());
                """, new { Id = id, Rule = rule, Contract = contract });
            var queue = new PostgresAlertWebhookQueue(new PostgresDatabaseConnectionFactory(fixture.ConnectionString));
            var original = (await queue.ClaimAsync(CancellationToken.None)).EnsureValue()!;
            Assert.Null((await queue.ClaimAsync(CancellationToken.None)).EnsureValue());
            await connection.ExecuteAsync("UPDATE alert_webhook_outbox SET leased_until = now() - interval '1 second' WHERE notification_id = @Id", new { Id = id });
            var reclaimed = (await queue.ClaimAsync(CancellationToken.None)).EnsureValue()!;
            Assert.NotEqual(original.LeaseToken, reclaimed.LeaseToken);
            (await queue.CompleteAsync(original, true, CancellationToken.None)).EnsureSuccess();
            Assert.False(await connection.ExecuteScalarAsync<bool>("SELECT completed_at IS NOT NULL FROM alert_webhook_outbox WHERE notification_id = @Id", new { Id = id }));
            await connection.ExecuteAsync("UPDATE alert_webhook_outbox SET attempt_count = 10, leased_until = now() - interval '1 second' WHERE notification_id = @Id", new { Id = id });
            Assert.Null((await queue.ClaimAsync(CancellationToken.None)).EnsureValue());
            Assert.True(await connection.ExecuteScalarAsync<bool>("SELECT completed_at IS NOT NULL FROM alert_webhook_outbox WHERE notification_id = @Id", new { Id = id }));
        }

        [Fact]
        public async Task Managed_webhook_secrets_are_validated_and_protected_from_deletion()
        {
            var contract = await CreateContract();
            var rule = new AlertRuleRequest(contract, "Credentials", "DeadLetters", 1, WebhookUrl: "https://hooks.example.com/alert", WebhookSecretRef: "secret.alert-test");
            var id = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{id}", rule)).StatusCode);
            var secrets = new PostgresManagedSecretRepository(new PostgresDatabaseConnectionFactory(fixture.ConnectionString));
            Assert.True(await secrets.CreateAsync("secret.alert-test", "encrypted-test-value", CancellationToken.None));
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{id}", rule)).StatusCode);
            Assert.Equal(NuntivusServer.Application.Persistence.ManagedSecretDeleteResult.Referenced, await secrets.DeleteAsync("secret.alert-test", CancellationToken.None));
            Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{id}", rule with { Signal = "DestinationFailures" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/v1/alerts/rules/{id}", rule with { WebhookSecretRef = null })).StatusCode);
            Assert.Equal(NuntivusServer.Application.Persistence.ManagedSecretDeleteResult.Deleted, await secrets.DeleteAsync("secret.alert-test", CancellationToken.None));
        }

        private async Task<Guid> CreateContract()
        {
            await _client.PostAsJsonAsync("/v1/customers", new { id = "alert-customer", name = "Alert" });
            var response = await _client.PostAsJsonAsync("/v1/contracts", new CreateContractRequest("alert-customer", ["orders.created"],
                new HttpDestinationConfig("https://hooks.example.com/events", new HttpAuthentication("none")), SourceIds: [ContractsApiFixture.SourceId]));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ContractCreated>())!.Id;
        }
    }
}

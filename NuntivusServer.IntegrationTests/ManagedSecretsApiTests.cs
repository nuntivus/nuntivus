using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Infastructure.Persistence.Postgres;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(ContractsApiCollection))]
    public sealed class ManagedSecretsApiTests : IAsyncLifetime
    {
        private readonly ContractsApiFixture _fixture;
        private readonly HttpClient _client;

        public ManagedSecretsApiTests(ContractsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
        }

        [Fact]
        public async Task Versions_remain_encrypted_and_delete_is_blocked_while_contract_references_secret()
        {
            var created = await _client.PostAsJsonAsync("/v1/admin/secrets", new { name = "secret.acme.token", value = "first-value" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.DoesNotContain("first-value", await created.Content.ReadAsStringAsync());

            var updated = await _client.PutAsJsonAsync("/v1/admin/secrets/secret.acme.token", new { value = "second-value" });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var updateBody = await updated.Content.ReadAsStringAsync();
            Assert.Contains("currentVersion\":2", updateBody);
            Assert.DoesNotContain("second-value", updateBody);

            var listed = await _client.GetStringAsync("/v1/admin/secrets");
            Assert.Contains("\"currentVersion\":2", listed);
            Assert.DoesNotContain("second-value", listed);

            var versions = await _client.GetAsync("/v1/admin/secrets/secret.acme.token/versions");
            Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
            var history = await versions.Content.ReadAsStringAsync();
            Assert.Contains("\"version\":1", history);
            Assert.Contains("\"version\":2", history);
            Assert.DoesNotContain("first-value", history);

            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();
            var encrypted = (await connection.QueryAsync<string>(
                "SELECT encrypted_value FROM managed_secret_versions WHERE name = 'secret.acme.token' ORDER BY version")).ToArray();
            Assert.Equal(2, encrypted.Length);
            Assert.All(encrypted, value => Assert.StartsWith("v1.", value));
            Assert.DoesNotContain("second-value", encrypted[1]);
            var repository = new PostgresManagedSecretRepository(new PostgresDatabaseConnectionFactory(_fixture.ConnectionString));
            Assert.Equal(encrypted[1], await repository.GetEncryptedValueAsync("secret.acme.token", CancellationToken.None));

            var customer = await _client.PostAsJsonAsync("/v1/customers", new { id = "acme", name = "Acme" });
            Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
            var contract = await _client.PostAsJsonAsync("/v1/contracts", new CreateContractRequest(
                "acme", new[] { "orders.created" },
                new NatsDestinationConfig("nats://localhost:4222", "orders.created",
                    Credentials: new NatsCredentials(Token: new DestinationCredential("ref", "secret.acme.token")))));
            Assert.Equal(HttpStatusCode.Created, contract.StatusCode);

            var conflict = await _client.DeleteAsync("/v1/admin/secrets/secret.acme.token");
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            var contractId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM contracts WHERE customer_id = 'acme'");
            var eventId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await connection.ExecuteAsync(
                """
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@EventId, 'orders.created', @IdempotencyKey, @SourceId, @Now, @Now, '{}'::jsonb, '{}'::jsonb)
                """, new { EventId = eventId, IdempotencyKey = $"secret-{eventId:N}", SourceId = Guid.NewGuid(), Now = now });
            var deliveryId = Guid.NewGuid();
            await connection.ExecuteAsync(
                """
                INSERT INTO deliveries(id, event_id, contract_id, customer_id, event_type, payload,
                                       destination_type, destination_config, status, created_at, updated_at)
                VALUES (@DeliveryId, @EventId, @ContractId, 'acme', 'orders.created', '{}'::jsonb,
                        'Nats', '{"credentials":{"token":{"value":"secret.acme.token"}}}'::jsonb,
                        'Dead', @Now, @Now)
                """, new { DeliveryId = deliveryId, EventId = eventId, ContractId = contractId, Now = now });
            await connection.ExecuteAsync(
                "UPDATE contracts SET auth_ref = NULL, destination_config = '{}'::jsonb WHERE id = @ContractId",
                new { ContractId = contractId });
            var deliveryConflict = await _client.DeleteAsync("/v1/admin/secrets/secret.acme.token");
            Assert.Equal(HttpStatusCode.Conflict, deliveryConflict.StatusCode);

            await connection.ExecuteAsync("UPDATE deliveries SET status = 'Delivered' WHERE id = @DeliveryId", new { DeliveryId = deliveryId });
            var deleted = await _client.DeleteAsync("/v1/admin/secrets/secret.acme.token");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        [Fact]
        public async Task Delete_waits_for_inflight_contract_save_and_rechecks_references()
        {
            var name = "secret.acme.concurrent";
            (await _client.PostAsJsonAsync("/v1/admin/secrets", new { name, value = "value" })).EnsureSuccessStatusCode();
            (await _client.PostAsJsonAsync("/v1/customers", new { id = "acme", name = "Acme" })).EnsureSuccessStatusCode();

            var factory = new PostgresDatabaseConnectionFactory(_fixture.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var contract = Contract.Create("acme", new[] { "orders.created" }, null, null,
                new Destination(NuntivusServer.Domain.Contracts.DestinationType.Nats,
                    "{\"Credentials\":{\"Token\":{\"Kind\":\"ref\",\"Value\":\"secret.acme.concurrent\"}}}"),
                DateTimeOffset.UtcNow);
            (await new PostgresContractRepository(factory).InsertAsync(contract, uow, CancellationToken.None)).EnsureSuccess();

            var deleteTask = new PostgresManagedSecretRepository(factory).DeleteAsync(name, CancellationToken.None);
            await Task.Delay(150);
            Assert.False(deleteTask.IsCompleted);
            uow.Commit();

            Assert.Equal(ManagedSecretDeleteResult.Referenced, await deleteTask.WaitAsync(TimeSpan.FromSeconds(10)));
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM contracts WHERE id = @Id", new { contract.Id }));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM managed_secrets WHERE name = @Name", new { Name = name }));
        }

        [Fact]
        public async Task Contract_save_waiting_on_secret_deletion_rejects_removed_reference()
        {
            var name = "secret.acme.removed";
            (await _client.PostAsJsonAsync("/v1/admin/secrets", new { name, value = "value" })).EnsureSuccessStatusCode();
            (await _client.PostAsJsonAsync("/v1/customers", new { id = "acme", name = "Acme" })).EnsureSuccessStatusCode();

            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync("SELECT 1 FROM managed_secrets WHERE name = @Name FOR UPDATE",
                new { Name = name }, transaction);

            var factory = new PostgresDatabaseConnectionFactory(_fixture.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var contract = Contract.Create("acme", new[] { "orders.created" }, null, null,
                new Destination(NuntivusServer.Domain.Contracts.DestinationType.Nats,
                    "{\"Credentials\":{\"Token\":{\"Kind\":\"ref\",\"Value\":\"secret.acme.removed\"}}}"),
                DateTimeOffset.UtcNow);
            var saveTask = new PostgresContractRepository(factory).InsertAsync(contract, uow, CancellationToken.None);
            await Task.Delay(150);
            Assert.False(saveTask.IsCompleted);

            await connection.ExecuteAsync("DELETE FROM managed_secrets WHERE name = @Name", new { Name = name }, transaction);
            await transaction.CommitAsync();

            var save = await saveTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(save.IsFailure);
            Assert.Contains(name, save.Errors[0].Message);
            uow.Rollback();
            Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM contracts"));
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;
    }
}

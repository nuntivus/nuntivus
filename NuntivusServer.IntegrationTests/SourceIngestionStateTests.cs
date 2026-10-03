using Dapper;
using Npgsql;
using NuntivusServer.Infastructure.Persistence.Postgres;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(EventsApiCollection))]
    public sealed class SourceIngestionStateTests : IAsyncLifetime
    {
        private readonly EventsApiFixture _fixture;
        public SourceIngestionStateTests(EventsApiFixture fixture) => _fixture = fixture;
        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task LeaseIsExclusiveAndProgressRequiresTheCurrentOwner()
        {
            var id = Guid.NewGuid();
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.ExecuteAsync("""
                INSERT INTO sources(id, name, type, enabled, configuration, created_at, updated_at)
                VALUES (@Id, 'test', 'FOLDER', true, '{"path":"/tmp/events"}'::jsonb, now(), now())
                """, new { Id = id });
            var repository = new PostgresSourceIngestionStateRepository(
                new PostgresDatabaseConnectionFactory(_fixture.ConnectionString));
            var first = (await repository.TryAcquireAsync(id.ToString("D"), "worker-a", TimeSpan.FromMinutes(1), CancellationToken.None)).Value!;
            Assert.Null((await repository.TryAcquireAsync(id.ToString("D"), "worker-b", TimeSpan.FromMinutes(1), CancellationToken.None)).Value);
            Assert.True((await repository.MarkProcessedAsync(first, "file-1", CancellationToken.None)).Value);
            Assert.True((await repository.HasProcessedAsync(id.ToString("D"), "file-1", CancellationToken.None)).Value);
            await repository.ReleaseAsync(first, CancellationToken.None);
            var second = (await repository.TryAcquireAsync(id.ToString("D"), "worker-b", TimeSpan.FromMinutes(1), CancellationToken.None)).Value!;
            Assert.False((await repository.MarkProcessedAsync(first, "file-2", CancellationToken.None)).Value);
            Assert.True((await repository.MarkProcessedAsync(second, "file-1", CancellationToken.None)).Value);
            await connection.ExecuteAsync("UPDATE sources SET enabled = false WHERE id = @Id", new { Id = id });
            Assert.False((await repository.RenewAsync(second, TimeSpan.FromMinutes(1), CancellationToken.None)).Value);
        }
    }
}

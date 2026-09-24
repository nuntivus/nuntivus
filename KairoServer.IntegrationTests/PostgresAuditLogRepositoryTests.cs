using Dapper;
using KairoServer.Application.Auditing;
using KairoServer.Infastructure.Persistence.Postgres;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(DeliveriesCollection))]
    public sealed class PostgresAuditLogRepositoryTests
    {
        private readonly DeliveriesFixture _fx;

        public PostgresAuditLogRepositoryTests(DeliveriesFixture fx) => _fx = fx;

        [Fact]
        public async Task ListAsync_Materializes_Timestamps_As_Utc_Offsets()
        {
            var id = Guid.NewGuid();
            var createdAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var expiresAt = createdAt.AddDays(30);
            var category = $"audit-test-{id}";

            await using (var connection = new NpgsqlConnection(_fx.ConnectionString))
            {
                await connection.OpenAsync();
                await connection.ExecuteAsync("""
                    INSERT INTO audit_logs (id, category, action, details, created_at, expires_at)
                    VALUES (@Id, @Category, 'created', '{}'::jsonb, @CreatedAt, @ExpiresAt);
                    """, new { Id = id, Category = category, CreatedAt = createdAt, ExpiresAt = expiresAt });
            }

            var repository = new PostgresAuditLogRepository(new PostgresDatabaseConnectionFactory(_fx.ConnectionString));
            var query = new AuditLogListQuery(category, null, null, null, null, null, null, null, null, null, "desc", 1, 10);

            var result = await repository.ListAsync(query, CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.ToString());
            var item = Assert.Single(result.Value.Items);
            Assert.Equal(id, item.Id);
            Assert.Equal(createdAt, item.CreatedAt);
            Assert.Equal(expiresAt, item.ExpiresAt);
            Assert.Equal(TimeSpan.Zero, item.CreatedAt.Offset);
            Assert.Equal(TimeSpan.Zero, item.ExpiresAt.Offset);
        }
    }
}

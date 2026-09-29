using Dapper;
using Npgsql;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresSourceRepository : ISourceRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresSourceRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Result> InsertAsync(Source source, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
        {
            try
            {
                var validation = await ManagedSecretReferenceValidator.ValidateAndLockAsync(
                    source.Configuration, null, unitOfWork, cancellationToken);
                if (validation.IsFailure) return validation;

                const string sql = """
                    INSERT INTO sources (id, name, description, type, enabled, configuration, created_at, updated_at)
                    VALUES (@Id, @Name, @Description, @Type, @Enabled, CAST(@Configuration AS jsonb), @CreatedAt, @UpdatedAt)
                    """;
                await unitOfWork.GetConnection()!.ExecuteAsync(new CommandDefinition(sql, Parameters(source),
                    unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                return Result.Success();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Result.Failure(Error.Conflict($"Source '{source.Id}' already exists."));
            }
            catch (Exception ex) { return Result.Failure(ex); }
        }

        public async Task<Result> UpdateAsync(Source source, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
        {
            try
            {
                var validation = await ManagedSecretReferenceValidator.ValidateAndLockAsync(
                    source.Configuration, null, unitOfWork, cancellationToken);
                if (validation.IsFailure) return validation;

                const string sql = """
                    UPDATE sources SET name = @Name, description = @Description, type = @Type,
                        enabled = @Enabled, configuration = CAST(@Configuration AS jsonb), updated_at = @UpdatedAt
                    WHERE id = @Id
                    """;
                var rows = await unitOfWork.GetConnection()!.ExecuteAsync(new CommandDefinition(sql, Parameters(source),
                    unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                return rows == 0 ? Result.Failure(Error.NotFound($"Source '{source.Id}' was not found.")) : Result.Success();
            }
            catch (Exception ex) { return Result.Failure(ex); }
        }

        public async Task<Result> DeleteAsync(string id, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
        {
            try
            {
                var rows = await unitOfWork.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM sources WHERE id = @Id AND NOT EXISTS (SELECT 1 FROM events WHERE source_id = @Id) AND NOT EXISTS (SELECT 1 FROM contract_sources WHERE source_id = @Id)", new { Id = Guid.Parse(id) },
                    unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                if (rows != 0) return Result.Success();
                var exists = await unitOfWork.GetConnection()!.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS(SELECT 1 FROM sources WHERE id = @Id)", new { Id = Guid.Parse(id) },
                    unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                return Result.Failure(exists
                    ? Error.Conflict("Source is referenced by events or contracts and cannot be deleted.")
                    : Error.NotFound($"Source '{id}' was not found."));
            }
            catch (PostgresException ex) when (ex.SqlState == "23503")
            {
                return Result.Failure(Error.Conflict("Source is referenced by contracts and cannot be deleted."));
            }
            catch (Exception ex) { return Result.Failure(ex); }
        }

        public async Task<Result<Source?>> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                var row = await connection.QuerySingleOrDefaultAsync<SourceRow>(new CommandDefinition(
                    "SELECT id, name, description, type, enabled, configuration::text AS configuration, created_at, updated_at FROM sources WHERE id = @Id",
                    new { Id = Guid.Parse(id) }, cancellationToken: cancellationToken));
                return Result.Success<Source?>(row?.ToDomain());
            }
            catch (Exception ex) { return Result.Failure<Source?>(ex); }
        }

        public async Task<Result<PagedResult<Source>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT count(*) FROM sources", cancellationToken: cancellationToken));
                var rows = await connection.QueryAsync<SourceRow>(new CommandDefinition(
                    "SELECT id, name, description, type, enabled, configuration::text AS configuration, created_at, updated_at FROM sources ORDER BY created_at DESC, id LIMIT @Limit OFFSET @Offset",
                    new { Limit = pageSize, Offset = (page - 1) * pageSize }, cancellationToken: cancellationToken));
                return Result.Success(PagedResult<Source>.Create(rows.Select(row => row.ToDomain()).ToList(), page, pageSize, count));
            }
            catch (Exception ex) { return Result.Failure<PagedResult<Source>>(ex); }
        }

        private static object Parameters(Source source) => new
        {
            Id = Guid.Parse(source.Id.Value), source.Name, source.Description,
            Type = source.Type.ToString(), source.Enabled, source.Configuration,
            source.CreatedAt, source.UpdatedAt
        };

        private sealed class SourceRow
        {
            public Guid id { get; set; }
            public string name { get; set; } = string.Empty;
            public string? description { get; set; }
            public string type { get; set; } = string.Empty;
            public bool enabled { get; set; }
            public string configuration { get; set; } = string.Empty;
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset updated_at { get; set; }

            public Source ToDomain() => Source.Rehydrate(id.ToString("D"), name, description,
                Enum.Parse<SourceType>(type), configuration, enabled, created_at, updated_at);
        }
    }
}

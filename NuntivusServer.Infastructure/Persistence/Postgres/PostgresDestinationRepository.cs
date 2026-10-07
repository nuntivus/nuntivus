using Dapper;
using Npgsql;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresDestinationRepository : IDestinationRepository
    {
        private readonly IDatabaseConnectionFactory _connections;

        public PostgresDestinationRepository(IDatabaseConnectionFactory connections) => _connections = connections;

        public async Task<Result<DestinationResource?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connections.CreateConnection();
                var row = await connection.QuerySingleOrDefaultAsync<DestinationRow>(new CommandDefinition(
                    "SELECT id, name, description, type, configuration::text AS configuration, enabled, configured, disable_policy, created_at, updated_at FROM destinations WHERE id = @Id",
                    new { Id = id }, cancellationToken: cancellationToken));
                return Result.Success<DestinationResource?>(row?.ToDomain());
            }
            catch (Exception ex) { return Result.Failure<DestinationResource?>(ex); }
        }

        public async Task<Result<PagedResult<DestinationResource>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = (NpgsqlConnection)_connections.CreateConnection();
                var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT count(*) FROM destinations", cancellationToken: cancellationToken));
                var rows = await connection.QueryAsync<DestinationRow>(new CommandDefinition(
                    "SELECT id, name, description, type, configuration::text AS configuration, enabled, configured, disable_policy, created_at, updated_at FROM destinations ORDER BY created_at DESC, id LIMIT @Limit OFFSET @Offset",
                    new { Limit = pageSize, Offset = (page - 1) * pageSize }, cancellationToken: cancellationToken));
                return Result.Success(PagedResult<DestinationResource>.Create(rows.Select(row => row.ToDomain()).ToList(), page, pageSize, count));
            }
            catch (Exception ex) { return Result.Failure<PagedResult<DestinationResource>>(ex); }
        }

        public async Task<Result> InsertAsync(DestinationResource destination, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
        {
            try
            {
                var validation = await ManagedSecretReferenceValidator.ValidateAndLockAsync(
                    destination.Destination.Configuration, null, unitOfWork, cancellationToken);
                if (validation.IsFailure) return validation;
                await unitOfWork.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO destinations (id, name, description, type, configuration, enabled, configured, disable_policy, created_at, updated_at) VALUES (@Id, @Name, @Description, @Type, CAST(@Configuration AS jsonb), @Enabled, @Configured, @DisablePolicy, @CreatedAt, @UpdatedAt)",
                    Parameters(destination), unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                return Result.Success();
            }
            catch (Exception ex) { return Result.Failure(ex); }
        }

        public async Task<Result> UpdateAsync(DestinationResource destination, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
        {
            try
            {
                var validation = await ManagedSecretReferenceValidator.ValidateAndLockAsync(
                    destination.Destination.Configuration, null, unitOfWork, cancellationToken);
                if (validation.IsFailure) return validation;
                var rows = await unitOfWork.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    "UPDATE destinations SET name = @Name, description = @Description, configuration = CAST(@Configuration AS jsonb), enabled = @Enabled, configured = @Configured, disable_policy = @DisablePolicy, updated_at = @UpdatedAt WHERE id = @Id",
                    Parameters(destination), unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                if (rows == 0) return Result.Failure(Error.NotFound($"Destination '{destination.Id}' was not found."));
                if (!destination.Enabled && destination.DisablePolicy == "pause")
                    await unitOfWork.GetConnection()!.ExecuteAsync(new CommandDefinition(
                        "UPDATE contract_destinations SET delivery_enabled = false WHERE destination_id = @Id",
                        new { destination.Id }, unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                else
                    await unitOfWork.GetConnection()!.ExecuteAsync(new CommandDefinition(
                        "UPDATE contract_destinations SET delivery_enabled = true WHERE destination_id = @Id",
                        new { destination.Id }, unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                return Result.Success();
            }
            catch (Exception ex) { return Result.Failure(ex); }
        }

        public async Task<Result> DeleteAsync(Guid id, bool removeBindings, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
        {
            try
            {
                if (removeBindings)
                    await unitOfWork.GetConnection()!.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM contract_destinations WHERE destination_id = @Id", new { Id = id },
                        unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                var rows = await unitOfWork.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM destinations WHERE id = @Id", new { Id = id },
                    unitOfWork.GetTransaction(), cancellationToken: cancellationToken));
                return rows == 0 ? Result.Failure(Error.NotFound($"Destination '{id}' was not found.")) : Result.Success();
            }
            catch (Exception ex) { return Result.Failure(ex); }
        }

        private static object Parameters(DestinationResource resource) => new
        {
            resource.Id, resource.Name, resource.Description,
            Type = resource.Destination.Type.ToString(), Configuration = resource.Destination.Configuration,
            resource.Enabled, resource.Configured, resource.DisablePolicy, resource.CreatedAt, resource.UpdatedAt
        };

        private sealed class DestinationRow
        {
            public Guid id { get; set; }
            public string name { get; set; } = "";
            public string? description { get; set; }
            public string type { get; set; } = "";
            public string configuration { get; set; } = "{}";
            public bool enabled { get; set; }
            public bool configured { get; set; }
            public string disable_policy { get; set; } = "continue";
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset updated_at { get; set; }
            public DestinationResource ToDomain() => new(id, name, description,
                new Destination(Enum.Parse<NuntivusServer.Domain.Contracts.DestinationType>(type), configuration),
                enabled, disable_policy, created_at, updated_at, configured);
        }
    }
}

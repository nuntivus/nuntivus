using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using NuntivusServer.Application.Persistence;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresManagedSecretRepository : IManagedSecretRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresManagedSecretRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IReadOnlyList<ManagedSecret>> ListAsync(CancellationToken cancellationToken)
        {
            await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<SecretRow>(new CommandDefinition(
                """
                SELECT s.name AS Name, s.created_at AS CreatedAt,
                       (SELECT max(v.version) FROM managed_secret_versions v WHERE v.name = s.name) AS CurrentVersion
                FROM managed_secrets s ORDER BY s.name
                """,
                cancellationToken: cancellationToken));
            return rows.Select(row => new ManagedSecret(row.Name, ToUtc(row.CreatedAt), row.CurrentVersion)).ToArray();
        }

        public async Task<bool> CreateAsync(string name, string encryptedValue, CancellationToken cancellationToken)
        {
            await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO managed_secrets (name) VALUES (@Name) ON CONFLICT (name) DO NOTHING",
                new { Name = name }, transaction,
                cancellationToken: cancellationToken));
            if (inserted != 1)
                return false;

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO managed_secret_versions (name, version, encrypted_value) VALUES (@Name, 1, @EncryptedValue)",
                new { Name = name, EncryptedValue = encryptedValue }, transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        public async Task<int?> AddVersionAsync(string name, string encryptedValue, CancellationToken cancellationToken)
        {
            await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var exists = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT name FROM managed_secrets WHERE name = @Name FOR UPDATE",
                new { Name = name }, transaction, cancellationToken: cancellationToken));
            if (exists is null)
                return null;

            var version = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COALESCE(max(version), 0) + 1 FROM managed_secret_versions WHERE name = @Name",
                new { Name = name }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO managed_secret_versions (name, version, encrypted_value) VALUES (@Name, @Version, @EncryptedValue)",
                new { Name = name, Version = version, EncryptedValue = encryptedValue }, transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return version;
        }

        public async Task<IReadOnlyList<ManagedSecretVersion>?> ListVersionsAsync(string name, CancellationToken cancellationToken)
        {
            await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS(SELECT 1 FROM managed_secrets WHERE name = @Name)",
                new { Name = name }, cancellationToken: cancellationToken));
            if (!exists)
                return null;

            var rows = await connection.QueryAsync<VersionRow>(new CommandDefinition(
                "SELECT version AS Version, created_at AS CreatedAt FROM managed_secret_versions WHERE name = @Name ORDER BY version DESC",
                new { Name = name }, cancellationToken: cancellationToken));
            return rows.Select(row => new ManagedSecretVersion(row.Version, ToUtc(row.CreatedAt))).ToArray();
        }

        public async Task<ManagedSecretDeleteResult> DeleteAsync(string name, CancellationToken cancellationToken)
        {
            await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var locked = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT name FROM managed_secrets WHERE name = @Name FOR UPDATE",
                new { Name = name }, transaction, cancellationToken: cancellationToken));
            if (locked is null)
                return ManagedSecretDeleteResult.NotFound;

            const string sql = """
                DELETE FROM managed_secrets s
                WHERE s.name = @Name
                  AND NOT EXISTS (
                    SELECT 1 FROM sources src
                    WHERE jsonb_path_exists(src.configuration, '$.** ? (@ == $secret)', jsonb_build_object('secret', @Name))
                  )
                  AND NOT EXISTS (
                    SELECT 1 FROM destinations dest
                    WHERE jsonb_path_exists(dest.configuration, '$.** ? (@ == $secret)', jsonb_build_object('secret', @Name))
                  )
                  AND NOT EXISTS (
                    SELECT 1 FROM contract_destinations cd
                    WHERE cd.auth_ref = @Name
                       OR jsonb_path_exists(cd.destination_config, '$.** ? (@ == $secret)', jsonb_build_object('secret', @Name))
                  )
                  AND NOT EXISTS (
                    SELECT 1 FROM deliveries d
                    WHERE d.status IN ('Pending', 'InFlight', 'Failed', 'Dead')
                      AND (d.auth_ref = @Name
                           OR jsonb_path_exists(d.destination_config, '$.** ? (@ == $secret)', jsonb_build_object('secret', @Name)))
                  )
                RETURNING s.name;
                """;
            var deleted = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                sql, new { Name = name }, transaction, cancellationToken: cancellationToken));
            if (deleted is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return ManagedSecretDeleteResult.Deleted;
            }

            return ManagedSecretDeleteResult.Referenced;
        }

        public async Task<string?> GetEncryptedValueAsync(string name, CancellationToken cancellationToken)
        {
            await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT encrypted_value FROM managed_secret_versions WHERE name = @Name ORDER BY version DESC LIMIT 1",
                new { Name = name }, cancellationToken: cancellationToken));
        }

        private static DateTimeOffset ToUtc(DateTime value) =>
            new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

        private sealed class SecretRow
        {
            public string Name { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public int CurrentVersion { get; set; }
        }

        private sealed class VersionRow
        {
            public int Version { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}

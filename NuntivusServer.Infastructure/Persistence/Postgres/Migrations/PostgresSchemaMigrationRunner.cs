using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Infastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace NuntivusServer.Infastructure.Persistence.Postgres.Migrations
{
    public sealed class PostgresSchemaMigrationRunner : IPostgresSchemaMigrationRunner
    {
        private static readonly Regex FileNameRegex = new(
            "^(?<version>\\d{3})_(?<name>[a-z0-9_]+)\\.sql$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex IdentifierRegex = new(
            "^[a-zA-Z_][a-zA-Z0-9_]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly IDatabaseConnectionFactory _connectionFactory;
        private readonly DatabaseMigrationsOption _option;
        private readonly ILogger<PostgresSchemaMigrationRunner> _logger;

        public PostgresSchemaMigrationRunner(
            IDatabaseConnectionFactory connectionFactory,
            IOptions<DatabaseMigrationsOption> option,
            ILogger<PostgresSchemaMigrationRunner> logger)
        {
            _connectionFactory = connectionFactory;
            _option = option.Value;
            _logger = logger;
        }

        public async Task<DatabaseMigrationRunResult> RunAsync(DatabaseMigrationMode mode, CancellationToken cancellationToken = default)
        {
            if (mode == DatabaseMigrationMode.Disabled)
            {
                return new DatabaseMigrationRunResult
                {
                    Mode = mode,
                    Success = true,
                };
            }

            var errors = new List<string>();
            var migrations = DiscoverMigrations(errors);
            if (errors.Count > 0)
            {
                return new DatabaseMigrationRunResult
                {
                    Mode = mode,
                    Success = false,
                    Errors = errors,
                    DiscoveredMigrations = migrations.Count,
                };
            }

            if (!TryQuoteIdentifier(_option.Schema, out var schemaIdentifier)
                || !TryQuoteIdentifier(_option.HistoryTable, out var tableIdentifier))
            {
                return new DatabaseMigrationRunResult
                {
                    Mode = mode,
                    Success = false,
                    Errors = ["DatabaseMigrations:Schema and DatabaseMigrations:HistoryTable must be valid SQL identifiers."],
                    DiscoveredMigrations = migrations.Count,
                };
            }

            await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await AcquireLockAsync(connection, cancellationToken);
            try
            {
                await EnsureHistoryTableAsync(connection, schemaIdentifier, tableIdentifier, cancellationToken);

                var applied = await LoadAppliedMigrationsAsync(connection, schemaIdentifier, tableIdentifier, cancellationToken);

                ValidateAppliedRows(applied, migrations, errors);
                var pending = DeterminePendingMigrations(migrations, applied, errors);

                if (errors.Count > 0)
                {
                    return new DatabaseMigrationRunResult
                    {
                        Mode = mode,
                        Success = false,
                        Errors = errors,
                        DiscoveredMigrations = migrations.Count,
                        PendingMigrations = pending.Count,
                    };
                }

                var appliedCount = 0;
                if (mode == DatabaseMigrationMode.Apply)
                {
                    foreach (var migration in pending)
                    {
                        var started = DateTimeOffset.UtcNow;
                        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
                        await using var command = new NpgsqlCommand(migration.Sql, connection, tx);
                        await command.ExecuteNonQueryAsync(cancellationToken);

                        var elapsed = DateTimeOffset.UtcNow - started;
                        await UpsertHistoryAsync(
                            connection,
                            tx,
                            schemaIdentifier,
                            tableIdentifier,
                            migration,
                            (int)Math.Ceiling(elapsed.TotalMilliseconds),
                            cancellationToken);

                        await tx.CommitAsync(cancellationToken);
                        appliedCount++;
                        _logger.LogInformation("Applied database migration {FileName}.", migration.FileName);
                    }

                    pending = [];
                }
                else
                {
                    await BackfillMissingMetadataAsync(connection, schemaIdentifier, tableIdentifier, migrations, applied, cancellationToken);
                }

                if (pending.Count > 0 && _option.FailOnPendingMigrations)
                {
                    errors.Add($"Pending database migrations: {string.Join(", ", pending.Select(m => m.FileName))}");
                }

                return new DatabaseMigrationRunResult
                {
                    Mode = mode,
                    Success = errors.Count == 0,
                    Errors = errors,
                    DiscoveredMigrations = migrations.Count,
                    AppliedMigrations = appliedCount,
                    PendingMigrations = pending.Count,
                };
            }
            finally
            {
                await ReleaseLockAsync(connection, cancellationToken);
            }
        }

        private List<DiscoveredMigration> DiscoverMigrations(List<string> errors)
        {
            var path = ResolveMigrationPath();
            if (!Directory.Exists(path))
            {
                errors.Add($"Migration path not found: {path}");
                return [];
            }

            var migrations = new List<DiscoveredMigration>();
            foreach (var file in Directory.GetFiles(path, "*.sql", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                var match = FileNameRegex.Match(fileName);
                if (!match.Success)
                {
                    errors.Add($"Invalid migration filename '{fileName}'. Expected format NNN_description.sql.");
                    continue;
                }

                var version = int.Parse(match.Groups["version"].Value);
                var sql = File.ReadAllText(file);
                migrations.Add(new DiscoveredMigration(
                    version,
                    fileName,
                    ComputeSha256(sql),
                    sql));
            }

            return migrations
                .OrderBy(m => m.Version)
                .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string ResolveMigrationPath()
        {
            if (Path.IsPathRooted(_option.MigrationsPath))
            {
                return _option.MigrationsPath;
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _option.MigrationsPath));
        }

        private static string ComputeSha256(string content)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(bytes);
        }

        private static bool TryQuoteIdentifier(string identifier, out string quoted)
        {
            if (!IdentifierRegex.IsMatch(identifier))
            {
                quoted = string.Empty;
                return false;
            }

            quoted = $"\"{identifier}\"";
            return true;
        }

        private async Task EnsureHistoryTableAsync(
            NpgsqlConnection connection,
            string schemaIdentifier,
            string tableIdentifier,
            CancellationToken cancellationToken)
        {
            var fullyQualified = $"{schemaIdentifier}.{tableIdentifier}";

            await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {schemaIdentifier};", connection))
            {
                await createSchema.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var createTable = new NpgsqlCommand($@"
CREATE TABLE IF NOT EXISTS {fullyQualified}
(
    filename TEXT PRIMARY KEY,
    version INTEGER NULL,
    checksum TEXT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    execution_ms INTEGER NOT NULL DEFAULT 0
);", connection))
            {
                await createTable.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var alterTable = new NpgsqlCommand($@"
ALTER TABLE {fullyQualified} ADD COLUMN IF NOT EXISTS version INTEGER NULL;
ALTER TABLE {fullyQualified} ADD COLUMN IF NOT EXISTS checksum TEXT NULL;
ALTER TABLE {fullyQualified} ADD COLUMN IF NOT EXISTS execution_ms INTEGER NOT NULL DEFAULT 0;", connection);
            await alterTable.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task AcquireLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key);", connection);
            command.Parameters.AddWithValue("key", _option.AdvisoryLockKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task ReleaseLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key);", connection);
            command.Parameters.AddWithValue("key", _option.AdvisoryLockKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<Dictionary<string, AppliedMigrationRow>> LoadAppliedMigrationsAsync(
            NpgsqlConnection connection,
            string schemaIdentifier,
            string tableIdentifier,
            CancellationToken cancellationToken)
        {
            var fullyQualified = $"{schemaIdentifier}.{tableIdentifier}";
            await using var command = new NpgsqlCommand(
                $"SELECT filename, version, checksum FROM {fullyQualified};",
                connection);

            var rows = new Dictionary<string, AppliedMigrationRow>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows[reader.GetString(0)] = new AppliedMigrationRow(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2));
            }

            return rows;
        }

        private void ValidateAppliedRows(
            Dictionary<string, AppliedMigrationRow> applied,
            List<DiscoveredMigration> discovered,
            List<string> errors)
        {
            var discoveredByName = discovered.ToDictionary(m => m.FileName, StringComparer.OrdinalIgnoreCase);
            foreach (var row in applied.Values)
            {
                if (!discoveredByName.ContainsKey(row.FileName) && _option.RequireKnownAppliedMigrations)
                {
                    errors.Add($"Applied migration '{row.FileName}' is not present in migration files.");
                }
            }
        }

        private static List<DiscoveredMigration> DeterminePendingMigrations(
            List<DiscoveredMigration> migrations,
            Dictionary<string, AppliedMigrationRow> applied,
            List<string> errors)
        {
            var pending = new List<DiscoveredMigration>();
            foreach (var migration in migrations)
            {
                if (!applied.TryGetValue(migration.FileName, out var row))
                {
                    pending.Add(migration);
                    continue;
                }

                if (row.Version is { } version && version != migration.Version)
                {
                    errors.Add($"Migration '{migration.FileName}' has version mismatch in history (expected {migration.Version}, got {version}).");
                }

                if (!string.IsNullOrWhiteSpace(row.Checksum)
                    && !string.Equals(row.Checksum, migration.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Checksum mismatch for migration '{migration.FileName}'. Do not modify applied migration files.");
                }
            }

            return pending;
        }

        private static async Task UpsertHistoryAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction tx,
            string schemaIdentifier,
            string tableIdentifier,
            DiscoveredMigration migration,
            int executionMs,
            CancellationToken cancellationToken)
        {
            var fullyQualified = $"{schemaIdentifier}.{tableIdentifier}";
            await using var command = new NpgsqlCommand($@"
INSERT INTO {fullyQualified}(filename, version, checksum, applied_at, execution_ms)
VALUES (@filename, @version, @checksum, now(), @execution_ms)
ON CONFLICT (filename)
DO UPDATE SET
    version = EXCLUDED.version,
    checksum = EXCLUDED.checksum,
    execution_ms = EXCLUDED.execution_ms;", connection, tx);

            command.Parameters.AddWithValue("filename", migration.FileName);
            command.Parameters.AddWithValue("version", migration.Version);
            command.Parameters.AddWithValue("checksum", migration.Checksum);
            command.Parameters.AddWithValue("execution_ms", executionMs);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task BackfillMissingMetadataAsync(
            NpgsqlConnection connection,
            string schemaIdentifier,
            string tableIdentifier,
            List<DiscoveredMigration> discovered,
            Dictionary<string, AppliedMigrationRow> applied,
            CancellationToken cancellationToken)
        {
            var fullyQualified = $"{schemaIdentifier}.{tableIdentifier}";
            foreach (var migration in discovered)
            {
                if (!applied.TryGetValue(migration.FileName, out var row))
                {
                    continue;
                }

                if (row.Version is not null && !string.IsNullOrWhiteSpace(row.Checksum))
                {
                    continue;
                }

                await using var command = new NpgsqlCommand($@"
UPDATE {fullyQualified}
SET version = COALESCE(version, @version),
    checksum = COALESCE(checksum, @checksum)
WHERE filename = @filename;", connection);
                command.Parameters.AddWithValue("filename", migration.FileName);
                command.Parameters.AddWithValue("version", migration.Version);
                command.Parameters.AddWithValue("checksum", migration.Checksum);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private sealed record DiscoveredMigration(int Version, string FileName, string Checksum, string Sql);

        private sealed record AppliedMigrationRow(string FileName, int? Version, string? Checksum);
    }
}

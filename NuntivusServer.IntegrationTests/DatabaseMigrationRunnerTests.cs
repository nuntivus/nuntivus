using Dapper;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Infastructure.Configuration;
using NuntivusServer.Infastructure.Persistence.Postgres;
using NuntivusServer.Infastructure.Persistence.Postgres.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NuntivusServer.IntegrationTests
{
    public sealed class DatabaseMigrationRunnerTests
    {
        [Fact]
        public async Task Apply_IsIdempotent_AndTracksHistory()
        {
            await WithPostgresAsync(async connectionString =>
            {
                var migrationsDir = CreateTempMigrationDirectory();
                try
                {
                    await File.WriteAllTextAsync(Path.Combine(migrationsDir, "001_create_alpha.sql"), "CREATE TABLE alpha(id integer PRIMARY KEY);");
                    await File.WriteAllTextAsync(Path.Combine(migrationsDir, "002_create_beta.sql"), "CREATE TABLE beta(id integer PRIMARY KEY);");

                    var first = await RunAsync(connectionString, migrationsDir, DatabaseMigrationMode.Apply);
                    Assert.True(first.Success);
                    Assert.Equal(2, first.AppliedMigrations);

                    var second = await RunAsync(connectionString, migrationsDir, DatabaseMigrationMode.Apply);
                    Assert.True(second.Success);
                    Assert.Equal(0, second.AppliedMigrations);

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();
                    var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations;");
                    Assert.Equal(2, count);
                }
                finally
                {
                    Directory.Delete(migrationsDir, recursive: true);
                }
            });
        }

        [Fact]
        public async Task VersionedSecretsMigration_backfills_existing_secret_value()
        {
            await WithPostgresAsync(async connectionString =>
            {
                var migrationsDir = CreateTempMigrationDirectory();
                try
                {
                    var originalMigrations = Path.Combine(AppContext.BaseDirectory, "Migrations");
                    File.Copy(Path.Combine(originalMigrations, "003_create_secrets.sql"),
                        Path.Combine(migrationsDir, "001_create_secrets.sql"));
                    var initial = await RunAsync(connectionString, migrationsDir, DatabaseMigrationMode.Apply);
                    Assert.True(initial.Success);

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();
                    await conn.ExecuteAsync("INSERT INTO managed_secrets (name, encrypted_value) VALUES ('secret.acme.test', 'encrypted')");

                    File.Copy(Path.Combine(originalMigrations, "004_version_managed_secrets.sql"),
                        Path.Combine(migrationsDir, "002_version_managed_secrets.sql"));
                    var upgraded = await RunAsync(connectionString, migrationsDir, DatabaseMigrationMode.Apply);
                    Assert.True(upgraded.Success);

                    var value = await conn.ExecuteScalarAsync<string>(
                        "SELECT encrypted_value FROM managed_secret_versions WHERE name = 'secret.acme.test' AND version = 1");
                    Assert.Equal("encrypted", value);
                }
                finally
                {
                    Directory.Delete(migrationsDir, recursive: true);
                }
            });
        }

        [Fact]
        public async Task Validate_FailsWhenPendingMigrationExists()
        {
            await WithPostgresAsync(async connectionString =>
            {
                var migrationsDir = CreateTempMigrationDirectory();
                try
                {
                    await File.WriteAllTextAsync(Path.Combine(migrationsDir, "001_create_alpha.sql"), "CREATE TABLE alpha(id integer PRIMARY KEY);");
                    var applied = await RunAsync(connectionString, migrationsDir, DatabaseMigrationMode.Apply);
                    Assert.True(applied.Success);

                    await File.WriteAllTextAsync(Path.Combine(migrationsDir, "002_create_beta.sql"), "CREATE TABLE beta(id integer PRIMARY KEY);");

                    var validation = await RunAsync(connectionString, migrationsDir, DatabaseMigrationMode.Validate);
                    Assert.False(validation.Success);
                    Assert.Contains(validation.Errors, e => e.Contains("Pending database migrations", StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    Directory.Delete(migrationsDir, recursive: true);
                }
            });
        }

        [Fact]
        public async Task Validate_FailsOnChecksumMismatch()
        {
            await WithPostgresAsync(async connectionString =>
            {
                var migrationsDir = CreateTempMigrationDirectory();
                try
                {
                    var migrationFile = Path.Combine(migrationsDir, "001_create_alpha.sql");
                    await File.WriteAllTextAsync(migrationFile, "CREATE TABLE alpha(id integer PRIMARY KEY);");

                    var applied = await RunAsync(connectionString, migrationsDir, DatabaseMigrationMode.Apply);
                    Assert.True(applied.Success);

                    await File.WriteAllTextAsync(migrationFile, "CREATE TABLE alpha(id bigint PRIMARY KEY);");

                    var validation = await RunAsync(connectionString, migrationsDir, DatabaseMigrationMode.Validate);
                    Assert.False(validation.Success);
                    Assert.Contains(validation.Errors, e => e.Contains("Checksum mismatch", StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    Directory.Delete(migrationsDir, recursive: true);
                }
            });
        }

        [Fact]
        public async Task Apply_FailsOnInvalidMigrationFileName()
        {
            await WithPostgresAsync(async connectionString =>
            {
                var migrationsDir = CreateTempMigrationDirectory();
                try
                {
                    await File.WriteAllTextAsync(Path.Combine(migrationsDir, "create_alpha.sql"), "CREATE TABLE alpha(id integer PRIMARY KEY);");

                    var result = await RunAsync(connectionString, migrationsDir, DatabaseMigrationMode.Apply);
                    Assert.False(result.Success);
                    Assert.Contains(result.Errors, e => e.Contains("Invalid migration filename", StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    Directory.Delete(migrationsDir, recursive: true);
                }
            });
        }

        private static string CreateTempMigrationDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "nuntivus-migrations-it", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static async Task<DatabaseMigrationRunResult> RunAsync(
            string connectionString,
            string migrationsPath,
            DatabaseMigrationMode mode)
        {
            var option = Options.Create(new DatabaseMigrationsOption
            {
                Mode = mode,
                MigrationsPath = migrationsPath,
            });

            IDatabaseConnectionFactory factory = new PostgresDatabaseConnectionFactory(connectionString);
            var runner = new PostgresSchemaMigrationRunner(factory, option, NullLogger<PostgresSchemaMigrationRunner>.Instance);
            return await runner.RunAsync(mode);
        }

        private static async Task WithPostgresAsync(Func<string, Task> action)
        {
            var postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("nuntivus_it")
                .WithUsername("nuntivus")
                .WithPassword("nuntivus")
                .Build();

            await postgres.StartAsync();
            try
            {
                await action(postgres.GetConnectionString());
            }
            finally
            {
                await postgres.DisposeAsync();
            }
        }
    }
}

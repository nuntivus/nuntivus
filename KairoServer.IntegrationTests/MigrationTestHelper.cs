using KairoServer.Application.Persistence;
using KairoServer.Infastructure.Configuration;
using KairoServer.Infastructure.Persistence.Postgres;
using KairoServer.Infastructure.Persistence.Postgres.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KairoServer.IntegrationTests
{
    internal static class MigrationTestHelper
    {
        public static async Task ApplyAllAsync(string connectionString)
        {
            var migrationPath = Path.Combine(AppContext.BaseDirectory, "Migrations");
            await ApplyAsync(connectionString, migrationPath);
        }

        public static async Task<DatabaseMigrationRunResult> ApplyAsync(string connectionString, string migrationPath)
        {
            var options = Options.Create(new DatabaseMigrationsOption
            {
                Mode = DatabaseMigrationMode.Apply,
                MigrationsPath = migrationPath,
            });

            IDatabaseConnectionFactory factory = new PostgresDatabaseConnectionFactory(connectionString);
            var runner = new PostgresSchemaMigrationRunner(factory, options, NullLogger<PostgresSchemaMigrationRunner>.Instance);
            return await runner.RunAsync(DatabaseMigrationMode.Apply);
        }

        public static async Task<DatabaseMigrationRunResult> ValidateAsync(string connectionString, string migrationPath, bool failOnPending = true)
        {
            var options = Options.Create(new DatabaseMigrationsOption
            {
                Mode = DatabaseMigrationMode.Validate,
                MigrationsPath = migrationPath,
                FailOnPendingMigrations = failOnPending,
            });

            IDatabaseConnectionFactory factory = new PostgresDatabaseConnectionFactory(connectionString);
            var runner = new PostgresSchemaMigrationRunner(factory, options, NullLogger<PostgresSchemaMigrationRunner>.Instance);
            return await runner.RunAsync(DatabaseMigrationMode.Validate);
        }
    }
}

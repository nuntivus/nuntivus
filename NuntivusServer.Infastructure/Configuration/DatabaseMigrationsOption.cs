using NuntivusServer.Infastructure.Persistence.Postgres.Migrations;

namespace NuntivusServer.Infastructure.Configuration
{
    public sealed class DatabaseMigrationsOption
    {
        public DatabaseMigrationMode Mode { get; set; } = DatabaseMigrationMode.Disabled;

        public string MigrationsPath { get; set; } = "Migrations";

        public string Schema { get; set; } = "public";

        public string HistoryTable { get; set; } = "schema_migrations";

        public long AdvisoryLockKey { get; set; } = 20260910;

        public bool FailOnPendingMigrations { get; set; } = true;

        public bool RequireKnownAppliedMigrations { get; set; } = true;
    }
}

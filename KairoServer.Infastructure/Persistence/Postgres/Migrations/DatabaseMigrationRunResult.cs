namespace KairoServer.Infastructure.Persistence.Postgres.Migrations
{
    public sealed class DatabaseMigrationRunResult
    {
        public required DatabaseMigrationMode Mode { get; init; }

        public bool Success { get; init; }

        public int DiscoveredMigrations { get; init; }

        public int AppliedMigrations { get; init; }

        public int PendingMigrations { get; init; }

        public IReadOnlyList<string> Errors { get; init; } = [];

        public string ToErrorMessage()
        {
            if (Errors.Count == 0)
            {
                return "Database migration validation failed.";
            }

            return string.Join("; ", Errors);
        }
    }
}

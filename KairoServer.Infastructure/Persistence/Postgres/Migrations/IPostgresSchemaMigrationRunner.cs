namespace KairoServer.Infastructure.Persistence.Postgres.Migrations
{
    public interface IPostgresSchemaMigrationRunner
    {
        Task<DatabaseMigrationRunResult> RunAsync(DatabaseMigrationMode mode, CancellationToken cancellationToken = default);
    }
}

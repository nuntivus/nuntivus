namespace NuntivusServer.Infastructure.Persistence.Postgres.Migrations
{
    public enum DatabaseMigrationMode
    {
        Disabled = 0,
        Validate = 1,
        Apply = 2,
    }
}

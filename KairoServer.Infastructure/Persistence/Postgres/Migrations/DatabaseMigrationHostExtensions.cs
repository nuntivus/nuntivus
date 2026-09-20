using KairoServer.Infastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KairoServer.Infastructure.Persistence.Postgres.Migrations
{
    public static class DatabaseMigrationHostExtensions
    {
        public static async Task EnsureDatabaseMigrationsAsync(
            this IServiceProvider services,
            DatabaseMigrationMode? modeOverride = null,
            CancellationToken cancellationToken = default)
        {
            await using var scope = services.CreateAsyncScope();
            var scopedServices = scope.ServiceProvider;
            var options = scopedServices.GetRequiredService<IOptions<DatabaseMigrationsOption>>().Value;
            var mode = modeOverride ?? options.Mode;
            if (mode == DatabaseMigrationMode.Disabled)
            {
                return;
            }

            var logger = scopedServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("DatabaseMigrations");
            var runner = scopedServices.GetRequiredService<IPostgresSchemaMigrationRunner>();

            var result = await runner.RunAsync(mode, cancellationToken);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.ToErrorMessage());
            }

            logger.LogInformation(
                "Database migrations finished (mode={Mode}, discovered={Discovered}, applied={Applied}, pending={Pending}).",
                mode,
                result.DiscoveredMigrations,
                result.AppliedMigrations,
                result.PendingMigrations);
        }
    }
}

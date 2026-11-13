using Dapper;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Security;
using NuntivusServer.Domain.Auditing;
using NuntivusServer.Common.Extensions;
using System.Text;
using System.Text.Json;

namespace NuntivusServer.Api.Security
{
    public static class LocalAdminBootstrap
    {
        public static async Task RunAsync(IServiceProvider services, string? username, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Trim().Length > 200)
                throw new InvalidOperationException("Set AdminBootstrap__Username to a nonempty username of at most 200 characters.");
            Console.Error.WriteLine("Enter the initial admin password (at least 12 characters):");
            var password = ReadPassword();
            if (password.Length < 12 || password.Length > 1024 || password != password.Trim())
                throw new InvalidOperationException("Use a 12–1024 character password without leading/trailing whitespace.");

            using var scope = services.CreateScope();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var now = DateTimeOffset.UtcNow;
            var subject = username.Trim();
            var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password);
            unit.Begin(true);
            try
            {
                var connection = unit.GetConnection()!;
                var transaction = unit.GetTransaction();
                await connection.ExecuteAsync(new CommandDefinition("LOCK TABLE admin_users IN EXCLUSIVE MODE;",
                    transaction: transaction, cancellationToken: token));
                var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT COUNT(*) FROM admin_users;", transaction: transaction, cancellationToken: token));
                if (count != 0) throw new InvalidOperationException("Admin bootstrap is only allowed before any admin users exist. Use authenticated administration for existing installations.");

                (await scope.ServiceProvider.GetRequiredService<IAdminUserRoleRepository>()
                    .ReplaceAsync(subject, subject, ["Admin"], now, unit, token)).EnsureSuccess();
                (await scope.ServiceProvider.GetRequiredService<IAdminLocalCredentialRepository>()
                    .UpsertAsync(subject, hash, now, unit, token)).EnsureSuccess();
                var audit = AuditLogEntry.Create("admin-user", "initial-admin-created", "local-bootstrap",
                    null, null, null, null, null, JsonSerializer.Serialize(new { subject }), now, now.AddYears(10));
                (await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().AddAsync(audit, unit, token)).EnsureSuccess();
                unit.Commit();
                Console.Error.WriteLine("Initial admin created. The password was not logged.");
            }
            catch { unit.Rollback(); throw; }
        }

        private static string ReadPassword()
        {
            if (Console.IsInputRedirected) return Console.ReadLine() ?? string.Empty;
            var password = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) { Console.Error.WriteLine(); return password.ToString(); }
                if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
                else if (!char.IsControl(key.KeyChar) && password.Length < 1025) password.Append(key.KeyChar);
            }
        }
    }
}

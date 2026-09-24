using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Security;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresAdminUserRoleRepository : IAdminUserRoleRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresAdminUserRoleRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Result<IReadOnlyList<AdminUserRoleAssignment>>> ListAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        u.subject AS Subject,
                        u.display_name AS DisplayName,
                        u.created_at AS CreatedAt,
                        u.updated_at AS UpdatedAt,
                        COALESCE(array_agg(r.role ORDER BY r.role) FILTER (WHERE r.role IS NOT NULL), ARRAY[]::text[]) AS roles
                    FROM admin_users u
                    LEFT JOIN admin_user_roles r ON r.subject = u.subject
                    GROUP BY u.subject, u.display_name, u.created_at, u.updated_at
                    ORDER BY u.updated_at DESC, u.subject ASC;
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var rows = await connection.QueryAsync<ListRow>(new CommandDefinition(
                    sql,
                    cancellationToken: cancellationToken));

                var assignments = rows
                    .Select(static row => new AdminUserRoleAssignment(
                        row.Subject,
                        row.DisplayName,
                        row.Roles ?? Array.Empty<string>(),
                        ToUtcDateTimeOffset(row.CreatedAt),
                        ToUtcDateTimeOffset(row.UpdatedAt)))
                    .ToArray();

                return Result.Success<IReadOnlyList<AdminUserRoleAssignment>>(assignments);
            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<AdminUserRoleAssignment>>(ex);
            }
        }

        public async Task<Result> ReplaceAsync(
            string subject,
            string? displayName,
            IReadOnlyList<string> roles,
            DateTimeOffset now,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                const string upsertUserSql = """
                    INSERT INTO admin_users(subject, display_name, created_at, updated_at)
                    VALUES (@Subject, @DisplayName, @Now, @Now)
                    ON CONFLICT (subject)
                    DO UPDATE SET
                        display_name = EXCLUDED.display_name,
                        updated_at = EXCLUDED.updated_at;
                    """;

                await connection.ExecuteAsync(new CommandDefinition(
                    upsertUserSql,
                    new
                    {
                        Subject = subject,
                        DisplayName = displayName,
                        Now = now,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                const string deleteRolesSql = """
                    DELETE FROM admin_user_roles
                    WHERE subject = @Subject;
                    """;

                await connection.ExecuteAsync(new CommandDefinition(
                    deleteRolesSql,
                    new { Subject = subject },
                    transaction,
                    cancellationToken: cancellationToken));

                var uniqueRoles = roles
                    .Where(static role => !string.IsNullOrWhiteSpace(role))
                    .Select(static role => role.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (uniqueRoles.Length > 0)
                {
                    const string insertRoleSql = """
                        INSERT INTO admin_user_roles(subject, role, created_at)
                        VALUES (@Subject, @Role, @Now)
                        ON CONFLICT (subject, role) DO NOTHING;
                        """;

                    foreach (var role in uniqueRoles)
                    {
                        await connection.ExecuteAsync(new CommandDefinition(
                            insertRoleSql,
                            new
                            {
                                Subject = subject,
                                Role = role,
                                Now = now,
                            },
                            transaction,
                            cancellationToken: cancellationToken));
                    }
                }

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result<bool>> DeleteBySubjectAsync(
            string subject,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    DELETE FROM admin_users
                    WHERE subject = @Subject;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new { Subject = subject },
                    transaction,
                    cancellationToken: cancellationToken));

                return Result.Success(rows > 0);
            }
            catch (Exception ex)
            {
                return Result.Failure<bool>(ex);
            }
        }

        public async Task<Result<AdminUserRoleAssignment?>> GetBySubjectAsync(
            string subject,
            CancellationToken cancellationToken)
        {
            try
            {
                const string userSql = """
                    SELECT subject as Subject, display_name as DisplayName, created_at as CreatedAt, updated_at as UpdatedAt
                    FROM admin_users
                    WHERE subject = @Subject
                    LIMIT 1;
                    """;

                const string rolesSql = """
                    SELECT role
                    FROM admin_user_roles
                    WHERE subject = @Subject
                    ORDER BY role ASC;
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var user = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
                    userSql,
                    new { Subject = subject },
                    cancellationToken: cancellationToken));

                if (user is null)
                    return Result.Success<AdminUserRoleAssignment?>(null);

                var roles = (await connection.QueryAsync<string>(new CommandDefinition(
                    rolesSql,
                    new { Subject = subject },
                    cancellationToken: cancellationToken))).ToArray();

                return Result.Success<AdminUserRoleAssignment?>(
                    new AdminUserRoleAssignment(
                        user.Subject,
                        user.DisplayName,
                        roles,
                        ToUtcDateTimeOffset(user.CreatedAt),
                        ToUtcDateTimeOffset(user.UpdatedAt)));
            }
            catch (Exception ex)
            {
                return Result.Failure<AdminUserRoleAssignment?>(ex);
            }
        }

        private sealed record UserRow(
            string Subject,
            string? DisplayName,
            DateTime CreatedAt,
            DateTime UpdatedAt);

        private sealed record ListRow(
            string Subject,
            string? DisplayName,
            DateTime CreatedAt,
            DateTime UpdatedAt,
            string[] Roles);

        private static DateTimeOffset ToUtcDateTimeOffset(DateTime value)
            => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
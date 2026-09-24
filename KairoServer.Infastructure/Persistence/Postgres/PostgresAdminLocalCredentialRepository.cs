using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresAdminLocalCredentialRepository : IAdminLocalCredentialRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresAdminLocalCredentialRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Result<AdminLocalCredential?>> GetBySubjectAsync(
            string subject,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        subject AS Subject,
                        password_hash AS PasswordHash,
                        created_at AS CreatedAt,
                        updated_at AS UpdatedAt
                    FROM admin_local_credentials
                    WHERE subject = @Subject
                    LIMIT 1;
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
                    sql,
                    new { Subject = subject },
                    cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<AdminLocalCredential?>(null);

                return Result.Success<AdminLocalCredential?>(new AdminLocalCredential(
                    row.Subject,
                    row.PasswordHash,
                    new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)),
                    new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc))));
            }
            catch (Exception ex)
            {
                return Result.Failure<AdminLocalCredential?>(ex);
            }
        }

        public async Task<Result> UpsertAsync(
            string subject,
            string passwordHash,
            DateTimeOffset now,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO admin_local_credentials(subject, password_hash, created_at, updated_at)
                    VALUES (@Subject, @PasswordHash, @Now, @Now)
                    ON CONFLICT (subject)
                    DO UPDATE SET
                        password_hash = EXCLUDED.password_hash,
                        updated_at = EXCLUDED.updated_at;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Subject = subject,
                        PasswordHash = passwordHash,
                        Now = now,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        private sealed record Row(
            string Subject,
            string PasswordHash,
            DateTime CreatedAt,
            DateTime UpdatedAt);
    }
}

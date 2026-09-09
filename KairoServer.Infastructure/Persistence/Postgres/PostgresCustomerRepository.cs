using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Customers;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public class PostgresCustomerRepository : ICustomerRepository
    {
        private const string UniqueViolationSqlState = "23505";

        public async Task<Result> InsertAsync(
            Customer customer,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO customers
                    (
                        id,
                        name,
                        enabled,
                        created_at,
                        updated_at
                    )
                    VALUES
                    (
                        @Id,
                        @Name,
                        @Enabled,
                        @CreatedAt,
                        @UpdatedAt
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            customer.Id,
                            customer.Name,
                            customer.Enabled,
                            customer.CreatedAt,
                            customer.UpdatedAt
                        },
                        transaction,
                        cancellationToken: cancellationToken));

                return Result.Success();
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == UniqueViolationSqlState)
            {
                return Result.Failure(Error.Conflict($"Customer with id '{customer.Id}' already exists."));
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result> UpdateAsync(
            Customer customer,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    UPDATE customers
                    SET name = @Name,
                        enabled = @Enabled,
                        updated_at = @UpdatedAt
                    WHERE id = @Id;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                var rows = await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            customer.Id,
                            customer.Name,
                            customer.Enabled,
                            customer.UpdatedAt
                        },
                        transaction,
                        cancellationToken: cancellationToken));

                return rows == 0
                    ? Result.Failure(Error.NotFound($"Customer '{customer.Id}' was not found."))
                    : Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result<Customer?>> GetByIdAsync(
            string id,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT id, name, enabled, created_at, updated_at
                    FROM customers
                    WHERE id = @Id
                    LIMIT 1;
                    """;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<CustomerRow>(
                    new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<Customer?>(null);

                return Result.Success<Customer?>(row.ToDomain());
            }
            catch (Exception ex)
            {
                return Result.Failure<Customer?>(ex);
            }
        }

        public async Task<Result<PagedResult<Customer>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT id, name, enabled, created_at, updated_at,
                           COUNT(*) OVER() AS total_count
                    FROM customers
                    ORDER BY created_at DESC, id ASC
                    LIMIT @PageSize OFFSET @Offset;
                    """;

                var offset = (page - 1) * pageSize;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var rows = await connection.QueryAsync<CustomerRowWithCount>(
                    new CommandDefinition(
                        sql,
                        new { PageSize = pageSize, Offset = offset },
                        cancellationToken: cancellationToken));

                var materialized = new List<Customer>();
                long totalCount = 0;
                foreach (var row in rows)
                {
                    totalCount = row.total_count;
                    materialized.Add(row.ToDomain());
                }

                return Result.Success(PagedResult<Customer>.Create(materialized, page, pageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<Customer>>(ex);
            }
        }

        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresCustomerRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        private NpgsqlConnection CreateConnection() =>
            (NpgsqlConnection)_connectionFactory.CreateConnection();

        private sealed class CustomerRow
        {
            public string id { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public bool enabled { get; set; }
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset updated_at { get; set; }

            public Customer ToDomain() =>
                Customer.Rehydrate(id, name, enabled, created_at, updated_at);
        }

        private sealed class CustomerRowWithCount
        {
            public string id { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public bool enabled { get; set; }
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset updated_at { get; set; }
            public long total_count { get; set; }

            public Customer ToDomain() =>
                Customer.Rehydrate(id, name, enabled, created_at, updated_at);
        }
    }
}

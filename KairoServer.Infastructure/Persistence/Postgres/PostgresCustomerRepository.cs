using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Customers;
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
            CustomerListQuery query,
            CancellationToken cancellationToken)
        {
            try
            {
                var where = new StringBuilder(" WHERE 1 = 1");
                var parameters = new DynamicParameters();

                if (query.Enabled.HasValue)
                {
                    where.Append(" AND c.enabled = @Enabled");
                    parameters.Add("Enabled", query.Enabled.Value);
                }

                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    where.Append(" AND (c.id ILIKE @Search OR c.name ILIKE @Search)");
                    parameters.Add("Search", $"%{query.Search.Trim()}%");
                }

                if (query.CreatedFrom.HasValue)
                {
                    where.Append(" AND c.created_at >= @CreatedFrom");
                    parameters.Add("CreatedFrom", query.CreatedFrom.Value);
                }

                if (query.CreatedTo.HasValue)
                {
                    where.Append(" AND c.created_at <= @CreatedTo");
                    parameters.Add("CreatedTo", query.CreatedTo.Value);
                }

                var sortColumn = ResolveSortColumn(query.SortBy);
                var sortDirection = string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                    ? "ASC"
                    : "DESC";

                var countSql = "SELECT COUNT(*) FROM customers c" + where;
                parameters.Add("Offset", (query.Page - 1) * query.PageSize);
                parameters.Add("PageSize", query.PageSize);

                var pageSql = $"""
                    SELECT
                        c.id,
                        c.name,
                        c.enabled,
                        c.created_at,
                        c.updated_at
                    FROM customers c
                    {where}
                    ORDER BY {sortColumn} {sortDirection}, c.id {sortDirection}
                    OFFSET @Offset
                    LIMIT @PageSize;
                    """;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var totalCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    countSql,
                    parameters,
                    cancellationToken: cancellationToken));

                var rows = await connection.QueryAsync<CustomerRow>(
                    new CommandDefinition(
                        pageSql,
                        parameters,
                        cancellationToken: cancellationToken));

                var materialized = new List<Customer>();
                foreach (var row in rows)
                {
                    materialized.Add(row.ToDomain());
                }

                return Result.Success(PagedResult<Customer>.Create(materialized, query.Page, query.PageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<Customer>>(ex);
            }
        }

        private static string ResolveSortColumn(string? sortBy)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return "c.created_at";

            if (string.Equals(sortBy, "id", StringComparison.OrdinalIgnoreCase))
                return "c.id";
            if (string.Equals(sortBy, "name", StringComparison.OrdinalIgnoreCase))
                return "c.name";
            if (string.Equals(sortBy, "updatedAt", StringComparison.OrdinalIgnoreCase))
                return "c.updated_at";

            return "c.created_at";
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

    }
}

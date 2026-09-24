using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Auditing;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Auditing;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresAuditLogRepository : IAuditLogRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresAuditLogRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<Result<int>> AddAsync(
            AuditLogEntry entry,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO audit_logs
                    (
                        id,
                        category,
                        action,
                        actor_id,
                        customer_id,
                        contract_id,
                        delivery_id,
                        event_id,
                        operation_id,
                        details,
                        created_at,
                        expires_at
                    )
                    VALUES
                    (
                        @Id,
                        @Category,
                        @Action,
                        @ActorId,
                        @CustomerId,
                        CAST(@ContractId AS uuid),
                        CAST(@DeliveryId AS uuid),
                        CAST(@EventId AS uuid),
                        CAST(@OperationId AS uuid),
                        CAST(@Details AS jsonb),
                        @CreatedAt,
                        @ExpiresAt
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        entry.Id,
                        entry.Category,
                        entry.Action,
                        entry.ActorId,
                        entry.CustomerId,
                        entry.ContractId,
                        entry.DeliveryId,
                        entry.EventId,
                        entry.OperationId,
                        entry.Details,
                        entry.CreatedAt,
                        entry.ExpiresAt,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                return Result.Success(rows);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
            }
        }

        public async Task<Result<PagedResult<AuditLogListItemView>>> ListAsync(
            AuditLogListQuery query,
            CancellationToken cancellationToken)
        {
            try
            {
                var where = new StringBuilder(" WHERE 1 = 1");
                var parameters = new DynamicParameters();

                if (!string.IsNullOrWhiteSpace(query.Category))
                {
                    where.Append(" AND a.category = @Category");
                    parameters.Add("Category", query.Category.Trim());
                }

                if (!string.IsNullOrWhiteSpace(query.Action))
                {
                    where.Append(" AND a.action = @Action");
                    parameters.Add("Action", query.Action.Trim());
                }

                if (!string.IsNullOrWhiteSpace(query.ActorId))
                {
                    where.Append(" AND a.actor_id ILIKE @ActorId");
                    parameters.Add("ActorId", $"%{query.ActorId.Trim()}%");
                }

                if (!string.IsNullOrWhiteSpace(query.CustomerId))
                {
                    where.Append(" AND a.customer_id = @CustomerId");
                    parameters.Add("CustomerId", query.CustomerId.Trim());
                }

                if (query.ContractId.HasValue)
                {
                    where.Append(" AND a.contract_id = @ContractId");
                    parameters.Add("ContractId", query.ContractId.Value);
                }

                if (query.DeliveryId.HasValue)
                {
                    where.Append(" AND a.delivery_id = @DeliveryId");
                    parameters.Add("DeliveryId", query.DeliveryId.Value);
                }

                if (query.EventId.HasValue)
                {
                    where.Append(" AND a.event_id = @EventId");
                    parameters.Add("EventId", query.EventId.Value);
                }

                if (query.OperationId.HasValue)
                {
                    where.Append(" AND a.operation_id = @OperationId");
                    parameters.Add("OperationId", query.OperationId.Value);
                }

                if (query.CreatedFrom.HasValue)
                {
                    where.Append(" AND a.created_at >= @CreatedFrom");
                    parameters.Add("CreatedFrom", query.CreatedFrom.Value);
                }

                if (query.CreatedTo.HasValue)
                {
                    where.Append(" AND a.created_at <= @CreatedTo");
                    parameters.Add("CreatedTo", query.CreatedTo.Value);
                }

                var sortDirection = string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                    ? "ASC"
                    : "DESC";

                const string countSqlTemplate = "SELECT COUNT(*) FROM audit_logs a";
                var countSql = countSqlTemplate + where;

                parameters.Add("Offset", (query.Page - 1) * query.PageSize);
                parameters.Add("PageSize", query.PageSize);

                var pageSql = $"""
                    SELECT
                        a.id            AS Id,
                        a.category      AS Category,
                        a.action        AS Action,
                        a.actor_id      AS ActorId,
                        a.customer_id   AS CustomerId,
                        a.contract_id   AS ContractId,
                        a.delivery_id   AS DeliveryId,
                        a.event_id      AS EventId,
                        a.operation_id  AS OperationId,
                        a.details::text AS Details,
                        a.created_at    AS CreatedAt,
                        a.expires_at    AS ExpiresAt
                    FROM audit_logs a
                    {where}
                    ORDER BY a.created_at {sortDirection}
                    LIMIT @PageSize OFFSET @Offset;
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var totalCount = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

                var rows = await connection.QueryAsync<AuditLogRow>(
                    new CommandDefinition(pageSql, parameters, cancellationToken: cancellationToken));

                return Result.Success(PagedResult<AuditLogListItemView>.Create(
                    rows.Select(row => new AuditLogListItemView(
                        row.Id,
                        row.Category,
                        row.Action,
                        row.ActorId,
                        row.CustomerId,
                        row.ContractId,
                        row.DeliveryId,
                        row.EventId,
                        row.OperationId,
                        row.Details,
                        new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)),
                        new DateTimeOffset(DateTime.SpecifyKind(row.ExpiresAt, DateTimeKind.Utc)))),
                    query.Page,
                    query.PageSize,
                    totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<AuditLogListItemView>>(ex);
            }
        }

        private sealed record AuditLogRow(
            Guid Id,
            string Category,
            string Action,
            string? ActorId,
            string? CustomerId,
            Guid? ContractId,
            Guid? DeliveryId,
            Guid? EventId,
            Guid? OperationId,
            string Details,
            DateTime CreatedAt,
            DateTime ExpiresAt);
    }
}

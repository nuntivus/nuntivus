using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Auditing;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresAuditLogRepository : IAuditLogRepository
    {
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
                        @ContractId,
                        @DeliveryId,
                        @EventId,
                        @OperationId,
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
    }
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresDeliveryRepository : IDeliveryRepository
    {
        public async Task<Result<int>> InsertPendingAsync(
            IReadOnlyCollection<Delivery> deliveries,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                if (deliveries.Count == 0)
                    return Result.Success(0);

                const string sql = """
                    INSERT INTO deliveries
                    (
                        id,
                        event_id,
                        contract_id,
                        customer_id,
                        event_type,
                        payload,
                        status,
                        created_at,
                        updated_at
                    )
                    VALUES
                    (
                        @Id,
                        @EventId,
                        @ContractId,
                        @CustomerId,
                        @EventType,
                        CAST(@Payload AS jsonb),
                        @Status,
                        @CreatedAt,
                        @UpdatedAt
                    )
                    ON CONFLICT (event_id, contract_id) DO NOTHING;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                var inserted = 0;
                foreach (var delivery in deliveries)
                {
                    var rows = await connection.ExecuteAsync(new CommandDefinition(
                        sql,
                        new
                        {
                            delivery.Id,
                            delivery.EventId,
                            delivery.ContractId,
                            delivery.CustomerId,
                            delivery.EventType,
                            delivery.Payload,
                            Status = delivery.Status.ToString(),
                            delivery.CreatedAt,
                            delivery.UpdatedAt,
                        },
                        transaction,
                        cancellationToken: cancellationToken));

                    inserted += rows;
                }

                return Result.Success(inserted);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
            }
        }
    }
}

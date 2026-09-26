using Dapper;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Events;
using NuntivusServer.Domain.Outbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public class PostgresOutboxRepository : IOutboxRepository
    {
        public async Task<Result> InsertAsync(OutboxMessage message, IUnitOfWork uow, CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO outbox_messages
                    (
                        id,
                        event_id,
                        type,
                        payload,
                        created_at,
                        processed_at,
                        attempt_count
                    )
                    VALUES
                    (
                        @Id,
                        @EventId,
                        @Type,
                        CAST(@Payload AS jsonb),
                        @CreatedAt,
                        NULL,
                        0
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        message.Id,
                        message.EventId,
                        message.Type,
                        message.Payload,
                        message.CreatedAt
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                return Result.Success();

            }
            catch(Exception ex)
            {
                return Result.Failure(ex);
            }
        }
    }
}

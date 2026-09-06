using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Events;
namespace KairoServer.Infastructure.Persistence.Postgres
{
    public class PostgresEventRepository : IEventRepository
    {
        public async Task<Result> InsertAsync(
            Event @event,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO events
                    (
                        id,
                        idempotency_key,
                        type,
                        source_id,
                        occurred_at,
                        received_at,
                        metadata,
                        payload
                    )
                    VALUES
                    (
                        @Id,
                        @IdempotencyKey,
                        @Type,
                        @SourceId,
                        @OccurredAt,
                        @ReceivedAt,
                        CAST(@Metadata AS jsonb),
                        CAST(@Payload AS jsonb)
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            @event.Id,
                            @event.IdempotencyKey,
                            @event.Type,
                            @event.SourceId,
                            @event.OccurredAt,
                            @event.ReceivedAt,
                            Metadata = @event.Metadata?.ToJson(),
                            @event.Payload
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
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresDeliveryRepository : IDeliveryRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresDeliveryRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

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
                        destination_type,
                        destination_config,
                        status,
                        attempt_count,
                        last_error,
                        next_attempt_at,
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
                        @DestinationType,
                        CAST(@DestinationConfiguration AS jsonb),
                        @Status,
                        @AttemptCount,
                        @LastError,
                        @NextAttemptAt,
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
                            DestinationType = delivery.DestinationType.ToString(),
                            delivery.DestinationConfiguration,
                            Status = delivery.Status.ToString(),
                            delivery.AttemptCount,
                            delivery.LastError,
                            delivery.NextAttemptAt,
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

        public async Task<Result<Delivery?>> GetByIdAsync(
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT
                        id,
                        event_id            AS EventId,
                        contract_id         AS ContractId,
                        customer_id         AS CustomerId,
                        event_type          AS EventType,
                        payload::text       AS Payload,
                        destination_type    AS DestinationType,
                        destination_config::text AS DestinationConfig,
                        status,
                        attempt_count       AS AttemptCount,
                        last_error          AS LastError,
                        next_attempt_at     AS NextAttemptAt,
                        created_at          AS CreatedAt,
                        updated_at          AS UpdatedAt
                    FROM deliveries
                    WHERE id = @Id;
                    """;

                using var connection = _connectionFactory.CreateConnection();
                connection.Open();

                var row = await connection.QuerySingleOrDefaultAsync<DeliveryRow>(new CommandDefinition(
                    sql,
                    new { Id = deliveryId },
                    cancellationToken: cancellationToken));

                if (row is null)
                {
                    return Result.Success<Delivery?>(null);
                }

                var delivery = Delivery.Rehydrate(
                    row.Id,
                    row.EventId,
                    row.ContractId,
                    row.CustomerId,
                    row.EventType,
                    row.Payload,
                    Enum.Parse<DestinationType>(row.DestinationType),
                    row.DestinationConfig,
                    Enum.Parse<DeliveryStatus>(row.Status),
                    row.AttemptCount,
                    row.LastError,
                    row.NextAttemptAt,
                    row.CreatedAt,
                    row.UpdatedAt);

                return Result.Success<Delivery?>(delivery);
            }
            catch (Exception ex)
            {
                return Result.Failure<Delivery?>(ex);
            }
        }

        public async Task<Result<bool>> UpdateAsync(
            Delivery delivery,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    UPDATE deliveries
                    SET status          = @Status,
                        attempt_count   = @AttemptCount,
                        last_error      = @LastError,
                        next_attempt_at = @NextAttemptAt,
                        updated_at      = @UpdatedAt
                    WHERE id = @Id;
                    """;

                var rows = await uow.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        delivery.Id,
                        Status = delivery.Status.ToString(),
                        delivery.AttemptCount,
                        delivery.LastError,
                        delivery.NextAttemptAt,
                        delivery.UpdatedAt,
                    },
                    uow.GetTransaction(),
                    cancellationToken: cancellationToken));

                return Result.Success(rows == 1);
            }
            catch (Exception ex)
            {
                return Result.Failure<bool>(ex);
            }
        }

        public async Task<Result<int>> AddAttemptAsync(
            DeliveryAttempt attempt,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO delivery_attempts
                    (
                        id,
                        delivery_id,
                        attempt_number,
                        succeeded,
                        failure_category,
                        error,
                        metadata,
                        started_at,
                        completed_at
                    )
                    VALUES
                    (
                        @Id,
                        @DeliveryId,
                        @AttemptNumber,
                        @Succeeded,
                        @FailureCategory,
                        @Error,
                        CAST(@Metadata AS jsonb),
                        @StartedAt,
                        @CompletedAt
                    )
                    ON CONFLICT (delivery_id, attempt_number) DO NOTHING;
                    """;

                var rows = await uow.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        attempt.Id,
                        attempt.DeliveryId,
                        attempt.AttemptNumber,
                        attempt.Succeeded,
                        attempt.FailureCategory,
                        attempt.Error,
                        Metadata = JsonSerializer.Serialize(attempt.Metadata),
                        attempt.StartedAt,
                        attempt.CompletedAt,
                    },
                    uow.GetTransaction(),
                    cancellationToken: cancellationToken));

                return Result.Success(rows);
            }
            catch (Exception ex)
            {
                return Result.Failure<int>(ex);
            }
        }

        private sealed class DeliveryRow
        {
            public Guid Id { get; set; }
            public Guid EventId { get; set; }
            public Guid ContractId { get; set; }
            public string CustomerId { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
            public string DestinationType { get; set; } = string.Empty;
            public string DestinationConfig { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int AttemptCount { get; set; }
            public string? LastError { get; set; }
            public DateTimeOffset? NextAttemptAt { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
        }
    }
}

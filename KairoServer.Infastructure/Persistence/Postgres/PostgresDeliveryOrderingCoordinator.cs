using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Deliveries;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresDeliveryOrderingCoordinator : IDeliveryOrderingCoordinator
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;
        private readonly DeliveryOrderingOption _options;

        public PostgresDeliveryOrderingCoordinator(
            IDatabaseConnectionFactory connectionFactory,
            DeliveryOrderingOption options)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<Result<DeliveryOrderingGateDecision>> TryAcquireAsync(
            Delivery delivery,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(delivery);

            if (!delivery.OrderingEnabled || string.IsNullOrWhiteSpace(delivery.OrderingKey))
                return Result.Success(DeliveryOrderingGateDecision.Allow());

            var now = DateTimeOffset.UtcNow;
            var leaseExpiresAt = now.Add(_options.LeaseDuration);

            try
            {
                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);
                await using var tx = await connection.BeginTransactionAsync(cancellationToken);

                var gate = await connection.QuerySingleAsync<GateDecisionRow>(new CommandDefinition(
                    """
                    WITH ensure_state AS (
                        INSERT INTO contract_delivery_ordering_state (ordering_key, active_delivery_id, lease_expires_at, updated_at)
                        VALUES (@OrderingKey, NULL, NULL, @Now)
                        ON CONFLICT (ordering_key) DO NOTHING
                    ),
                    state_row AS (
                        SELECT ordering_key,
                               active_delivery_id,
                               lease_expires_at
                        FROM contract_delivery_ordering_state
                        WHERE ordering_key = @OrderingKey
                        FOR UPDATE
                    ),
                    head_row AS (
                        SELECT id,
                               next_attempt_at
                        FROM deliveries
                        WHERE ordering_enabled = TRUE
                          AND ordering_key = @OrderingKey
                          AND (
                                status = @PendingStatus
                             OR status = @InFlightStatus
                             OR (status = @FailedStatus AND next_attempt_at IS NOT NULL)
                          )
                        ORDER BY created_at ASC, id ASC
                        LIMIT 1
                    ),
                    gate AS (
                        SELECT
                            CASE
                                WHEN s.active_delivery_id IS NOT NULL
                                     AND s.active_delivery_id <> @DeliveryId
                                     AND s.lease_expires_at IS NOT NULL
                                     AND s.lease_expires_at > @Now
                                    THEN FALSE
                                WHEN h.id IS NULL OR h.id <> @DeliveryId
                                    THEN FALSE
                                ELSE TRUE
                            END AS granted,
                            CASE
                                WHEN s.active_delivery_id IS NOT NULL
                                     AND s.active_delivery_id <> @DeliveryId
                                     AND s.lease_expires_at IS NOT NULL
                                     AND s.lease_expires_at > @Now
                                    THEN s.lease_expires_at
                                WHEN h.id IS NULL OR h.id <> @DeliveryId
                                    THEN h.next_attempt_at
                                ELSE NULL
                            END AS retry_after
                        FROM state_row s
                        LEFT JOIN head_row h ON TRUE
                    ),
                    claim AS (
                        UPDATE contract_delivery_ordering_state s
                        SET active_delivery_id = @DeliveryId,
                            lease_expires_at = @LeaseExpiresAt,
                            updated_at = @Now
                        FROM gate g
                        WHERE s.ordering_key = @OrderingKey
                          AND g.granted = TRUE
                    )
                    SELECT granted AS Granted,
                           retry_after AS RetryAfter
                    FROM gate;
                    """,
                    new
                    {
                        delivery.OrderingKey,
                        DeliveryId = delivery.Id,
                        LeaseExpiresAt = leaseExpiresAt,
                        Now = now,
                        PendingStatus = DeliveryStatus.Pending.ToString(),
                        InFlightStatus = DeliveryStatus.InFlight.ToString(),
                        FailedStatus = DeliveryStatus.Failed.ToString(),
                    },
                    tx,
                    cancellationToken: cancellationToken));

                await tx.CommitAsync(cancellationToken);
                return gate.Granted
                    ? Result.Success(DeliveryOrderingGateDecision.Allow())
                    : Result.Success(DeliveryOrderingGateDecision.Block(gate.RetryAfter));
            }
            catch (Exception ex)
            {
                return Result.Failure<DeliveryOrderingGateDecision>(ex);
            }
        }

        public async Task<Result> RecordOutcomeAsync(
            Delivery delivery,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(delivery);

            if (!delivery.OrderingEnabled || string.IsNullOrWhiteSpace(delivery.OrderingKey))
                return Result.Success();

            var now = DateTimeOffset.UtcNow;

            try
            {
                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var command = delivery.Status is DeliveryStatus.Failed && delivery.NextAttemptAt is { } nextAttemptAt
                    ? new CommandDefinition(
                        """
                        UPDATE contract_delivery_ordering_state
                        SET active_delivery_id = @ActiveDeliveryId,
                            lease_expires_at = @LeaseExpiresAt,
                            updated_at = @UpdatedAt
                        WHERE ordering_key = @OrderingKey
                          AND (active_delivery_id IS NULL OR active_delivery_id = @ActiveDeliveryId);
                        """,
                        new
                        {
                            delivery.OrderingKey,
                            ActiveDeliveryId = delivery.Id,
                            LeaseExpiresAt = (nextAttemptAt > now ? nextAttemptAt : now).Add(_options.LeaseDuration),
                            UpdatedAt = now,
                        },
                        cancellationToken: cancellationToken)
                    : new CommandDefinition(
                        """
                        UPDATE contract_delivery_ordering_state
                        SET active_delivery_id = NULL,
                            lease_expires_at = NULL,
                            updated_at = @UpdatedAt
                        WHERE ordering_key = @OrderingKey
                          AND (active_delivery_id IS NULL OR active_delivery_id = @ActiveDeliveryId);
                        """,
                        new
                        {
                            delivery.OrderingKey,
                            ActiveDeliveryId = delivery.Id,
                            UpdatedAt = now,
                        },
                        cancellationToken: cancellationToken);

                await connection.ExecuteAsync(command);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        private NpgsqlConnection CreateConnection() =>
            (NpgsqlConnection)_connectionFactory.CreateConnection();

        private sealed class GateDecisionRow
        {
            public bool Granted { get; set; }
            public DateTimeOffset? RetryAfter { get; set; }
        }
    }
}

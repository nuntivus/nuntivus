using Dapper;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresAlertWebhookQueue(IDatabaseConnectionFactory connections) : IAlertWebhookQueue
    {
        public async Task<Result<AlertWebhookMessage?>> ClaimAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var connection = connections.CreateConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();
                await connection.ExecuteAsync(new CommandDefinition("""
                    WITH expired AS (
                        SELECT notification_id, lease_token FROM alert_webhook_outbox
                        WHERE completed_at IS NULL AND leased_until < now() LIMIT 100 FOR UPDATE SKIP LOCKED
                    ) UPDATE alert_webhook_attempts a SET completed_at = now(), outcome = 'LeaseExpired', error = 'Webhook lease expired'
                    FROM expired e WHERE a.lease_token = e.lease_token AND a.outcome = 'InFlight';
                    WITH exhausted AS (
                        SELECT notification_id FROM alert_webhook_outbox
                        WHERE completed_at IS NULL AND attempt_count - retry_budget_offset >= 10 AND (leased_until IS NULL OR leased_until < now())
                        LIMIT 100 FOR UPDATE SKIP LOCKED
                    ) UPDATE alert_webhook_outbox q SET completed_at = now(), last_error = 'Webhook retry limit reached',
                        lease_token = NULL, leased_until = NULL
                    FROM exhausted e WHERE q.notification_id = e.notification_id
                    """, transaction: transaction, cancellationToken: cancellationToken));
                var message = await connection.QuerySingleOrDefaultAsync<AlertWebhookMessage>(new CommandDefinition("""
                    WITH candidate AS (
                        SELECT notification_id FROM alert_webhook_outbox
                        WHERE completed_at IS NULL AND attempt_count - retry_budget_offset < 10 AND next_attempt_at <= now() AND (leased_until IS NULL OR leased_until < now())
                        ORDER BY next_attempt_at LIMIT 1 FOR UPDATE SKIP LOCKED
                    ) UPDATE alert_webhook_outbox q SET lease_token = @Token, leased_until = now() + interval '1 minute', attempt_count = attempt_count + 1
                    FROM candidate c WHERE q.notification_id = c.notification_id
                    RETURNING q.notification_id AS NotificationId, q.lease_token AS LeaseToken, q.endpoint AS Endpoint,
                        q.secret_ref AS SecretRef, q.payload::text AS Payload, q.attempt_count AS AttemptCount
                    """, new { Token = Guid.NewGuid() }, transaction, cancellationToken: cancellationToken));
                if (message is not null)
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO alert_webhook_attempts (lease_token, notification_id, attempt_number, started_at, outcome)
                        VALUES (@LeaseToken, @NotificationId, @AttemptCount, now(), 'InFlight')
                        """, message, transaction, cancellationToken: cancellationToken));
                transaction.Commit();
                return Result.Success(message);
            }
            catch (Exception ex) { return Result.Failure<AlertWebhookMessage?>(ex); }
        }
        public async Task<Result> CompleteAsync(AlertWebhookMessage message, bool delivered, CancellationToken cancellationToken)
        {
            try
            {
                using var connection = connections.CreateConnection();
                await connection.ExecuteAsync(new CommandDefinition("""
                    WITH completed AS (UPDATE alert_webhook_outbox SET lease_token = NULL, leased_until = NULL,
                        completed_at = CASE WHEN @Delivered OR attempt_count - retry_budget_offset >= 10 THEN now() ELSE NULL END,
                        last_error = CASE WHEN @Delivered THEN NULL ELSE 'Webhook delivery failed' END,
                        next_attempt_at = now() + make_interval(secs => LEAST(3600, power(2, LEAST(attempt_count - retry_budget_offset, 12))::integer * 5))
                    WHERE notification_id = @Id AND lease_token = @Token AND leased_until > now()
                    RETURNING notification_id)
                    UPDATE alert_webhook_attempts SET completed_at = now(), outcome = CASE WHEN @Delivered THEN 'Succeeded' ELSE 'Failed' END,
                        error = CASE WHEN @Delivered THEN NULL ELSE 'Webhook delivery failed' END
                    WHERE lease_token = @Token AND notification_id IN (SELECT notification_id FROM completed)
                    """, new { Id = message.NotificationId, Token = message.LeaseToken, Delivered = delivered }, cancellationToken: cancellationToken));
                return Result.Success();
            }
            catch (Exception ex) { return Result.Failure(ex); }
        }
    }
}

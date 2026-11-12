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
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE alert_webhook_outbox SET completed_at = now(), last_error = 'Webhook retry limit reached',
                        lease_token = NULL, leased_until = NULL
                    WHERE completed_at IS NULL AND attempt_count >= 10 AND (leased_until IS NULL OR leased_until < now())
                    """, cancellationToken: cancellationToken));
                var message = await connection.QuerySingleOrDefaultAsync<AlertWebhookMessage>(new CommandDefinition("""
                    WITH candidate AS (
                        SELECT notification_id FROM alert_webhook_outbox
                        WHERE completed_at IS NULL AND attempt_count < 10 AND next_attempt_at <= now() AND (leased_until IS NULL OR leased_until < now())
                        ORDER BY next_attempt_at LIMIT 1 FOR UPDATE SKIP LOCKED
                    ) UPDATE alert_webhook_outbox q SET lease_token = @Token, leased_until = now() + interval '1 minute', attempt_count = attempt_count + 1
                    FROM candidate c WHERE q.notification_id = c.notification_id
                    RETURNING q.notification_id AS NotificationId, q.lease_token AS LeaseToken, q.endpoint AS Endpoint,
                        q.secret_ref AS SecretRef, q.payload::text AS Payload, q.attempt_count AS AttemptCount
                    """, new { Token = Guid.NewGuid() }, cancellationToken: cancellationToken));
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
                    UPDATE alert_webhook_outbox SET lease_token = NULL, leased_until = NULL,
                        completed_at = CASE WHEN @Delivered OR attempt_count >= 10 THEN now() ELSE NULL END,
                        last_error = CASE WHEN @Delivered THEN NULL ELSE 'Webhook delivery failed' END,
                        next_attempt_at = now() + make_interval(secs => @Backoff)
                    WHERE notification_id = @Id AND lease_token = @Token
                    """, new { Id = message.NotificationId, Token = message.LeaseToken, Delivered = delivered,
                    Backoff = Math.Min(3600, (int)Math.Pow(2, Math.Min(message.AttemptCount, 12)) * 5) }, cancellationToken: cancellationToken));
                return Result.Success();
            }
            catch (Exception ex) { return Result.Failure(ex); }
        }
    }
}

using System.Text.Json;
using Dapper;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Alerts;
using NuntivusServer.Domain.Auditing;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresAlertOperationsService(IDatabaseConnectionFactory connections, IUnitOfWork uow, IAuditLogRepository audit) : IAlertOperationsService
    {
        private const string StatusSql = """
            SELECT n.id AS NotificationId,
                CASE WHEN q.notification_id IS NULL THEN 'NotConfigured'
                     WHEN q.leased_until > now() THEN 'Sending'
                     WHEN q.completed_at IS NOT NULL AND q.last_error IS NULL THEN 'Succeeded'
                     WHEN q.completed_at IS NOT NULL OR q.attempt_count - q.retry_budget_offset >= 10 THEN 'Exhausted'
                     WHEN q.last_error IS NOT NULL OR q.leased_until < now() THEN 'Failed' ELSE 'Pending' END AS Status,
                COALESCE(q.attempt_count, 0) AS AttemptCount, COALESCE(q.attempt_count - q.retry_budget_offset, 0) AS CycleAttemptCount,
                CASE WHEN q.completed_at IS NULL THEN q.next_attempt_at END AS NextAttemptAt,
                q.leased_until AS LeasedUntil, q.completed_at AS CompletedAt, q.last_error AS LastError,
                COALESCE((q.completed_at IS NULL OR q.last_error IS NOT NULL) AND (q.leased_until IS NULL OR q.leased_until <= now()) AND
                    (q.last_error IS NOT NULL OR q.leased_until <= now() OR q.attempt_count - q.retry_budget_offset >= 10), false) AS CanRetry
            FROM alert_notifications n LEFT JOIN alert_webhook_outbox q ON q.notification_id = n.id WHERE n.id = @Id
            """;
        public async Task<Result<AlertWebhookStatusView>> GetWebhookAsync(Guid notificationId, CancellationToken cancellationToken)
        {
            try
            {
                using var connection = connections.CreateConnection();
                var view = await connection.QuerySingleOrDefaultAsync<AlertWebhookStatusView>(new CommandDefinition(StatusSql, new { Id = notificationId }, cancellationToken: cancellationToken));
                return view is null ? Result.Failure<AlertWebhookStatusView>(Error.NotFound("Notification was not found.")) : Result.Success(view);
            }
            catch (Exception ex) { return Result.Failure<AlertWebhookStatusView>(ex); }
        }
        public async Task<Result<PagedResult<AlertWebhookAttemptView>>> ListAttemptsAsync(Guid notificationId, int page, CancellationToken cancellationToken)
        {
            if (page is < 1 or > 1000000) return Result.Failure<PagedResult<AlertWebhookAttemptView>>(Error.Validation("Invalid page."));
            var exists = await GetWebhookAsync(notificationId, cancellationToken);
            if (exists.IsFailure) return Result<PagedResult<AlertWebhookAttemptView>>.Fail(exists.Errors);
            try
            {
                using var connection = connections.CreateConnection();
                using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
                    SELECT attempt_number AS AttemptNumber, started_at AS StartedAt, completed_at AS CompletedAt,
                        CASE WHEN outcome = 'InFlight' AND started_at <= now() - interval '1 minute' THEN 'LeaseExpired' ELSE outcome END AS Outcome,
                        error AS Error FROM alert_webhook_attempts WHERE notification_id = @Id ORDER BY attempt_number DESC LIMIT 50 OFFSET @Offset;
                    SELECT count(*) FROM alert_webhook_attempts WHERE notification_id = @Id;
                    """, new { Id = notificationId, Offset = (page - 1) * 50 }, cancellationToken: cancellationToken));
                return Result.Success(PagedResult<AlertWebhookAttemptView>.Create(await results.ReadAsync<AlertWebhookAttemptView>(), page, 50, await results.ReadSingleAsync<long>()));
            }
            catch (Exception ex) { return Result.Failure<PagedResult<AlertWebhookAttemptView>>(ex); }
        }
        public async Task<Result<AlertRuleView>> ArchiveRuleAsync(Guid id, string actorId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(actorId)) return Result.Failure<AlertRuleView>(Error.Unauthorized("Operator identity is required."));
            try
            {
                uow.Begin(true);
                var previous = await uow.GetConnection()!.QuerySingleOrDefaultAsync<AlertRuleView>(new CommandDefinition(
                    $"SELECT {PostgresAlertService.RuleColumns} FROM alert_rules WHERE id = @Id FOR UPDATE", new { Id = id }, uow.GetTransaction(), cancellationToken: cancellationToken));
                if (previous is null) { uow.Rollback(); return Result.Failure<AlertRuleView>(Error.NotFound("Rule was not found.")); }
                if (previous.Archived) { uow.Commit(); return Result.Success(previous); }
                var response = await uow.GetConnection()!.QuerySingleAsync<AlertRuleView>(new CommandDefinition(
                    $"UPDATE alert_rules SET archived = true, enabled = false, updated_at = now() WHERE id = @Id RETURNING {PostgresAlertService.RuleColumns}",
                    new { Id = id }, uow.GetTransaction(), cancellationToken: cancellationToken));
                await AuditAsync("rule_archived", actorId, response.ContractId, new { RuleId = id }, response, cancellationToken);
                uow.Commit(); return Result.Success(response);
            }
            catch (Exception ex) { uow.Rollback(); return Result.Failure<AlertRuleView>(ex); }
        }
        public async Task<Result<AlertWebhookStatusView>> RetryWebhookAsync(Guid notificationId, AlertWebhookRetryRequest request, string actorId, CancellationToken cancellationToken)
        {
            if (request.OperationId == Guid.Empty) return Result.Failure<AlertWebhookStatusView>(Error.Validation("Operation ID is required."));
            if (string.IsNullOrWhiteSpace(actorId)) return Result.Failure<AlertWebhookStatusView>(Error.Unauthorized("Operator identity is required."));
            try
            {
                uow.Begin(true);
                var locked = await uow.GetConnection()!.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                    "SELECT notification_id FROM alert_webhook_outbox WHERE notification_id = @Id FOR UPDATE", new { Id = notificationId }, uow.GetTransaction(), cancellationToken: cancellationToken));
                if (locked is null) { uow.Rollback(); return Result.Failure<AlertWebhookStatusView>(Error.NotFound("Webhook delivery was not found.")); }
                var current = await uow.GetConnection()!.QuerySingleAsync<AlertWebhookStatusView>(new CommandDefinition(StatusSql, new { Id = notificationId }, uow.GetTransaction(), cancellationToken: cancellationToken));
                var duplicate = await uow.GetConnection()!.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS(SELECT 1 FROM alert_webhook_retry_operations WHERE notification_id = @Id AND operation_id = @OperationId)",
                    new { Id = notificationId, request.OperationId }, uow.GetTransaction(), cancellationToken: cancellationToken));
                if (duplicate) { uow.Commit(); return Result.Success(current); }
                if (!current.CanRetry) { uow.Rollback(); return Result.Failure<AlertWebhookStatusView>(Error.Conflict("Only failed webhooks without an active send can be retried.")); }
                await uow.GetConnection()!.ExecuteAsync(new CommandDefinition("""
                    UPDATE alert_webhook_attempts SET completed_at = now(), outcome = 'LeaseExpired', error = 'Webhook lease expired'
                    WHERE notification_id = @Id AND outcome = 'InFlight';
                    UPDATE alert_webhook_outbox SET retry_budget_offset = attempt_count, next_attempt_at = now(), completed_at = NULL,
                        leased_until = NULL, lease_token = NULL, last_error = NULL WHERE notification_id = @Id;
                    INSERT INTO alert_webhook_retry_operations (notification_id, operation_id, requested_at, requested_by) VALUES (@Id, @OperationId, now(), @Actor);
                    """, new { Id = notificationId, request.OperationId, Actor = actorId }, uow.GetTransaction(), cancellationToken: cancellationToken));
                var response = await uow.GetConnection()!.QuerySingleAsync<AlertWebhookStatusView>(new CommandDefinition(StatusSql, new { Id = notificationId }, uow.GetTransaction(), cancellationToken: cancellationToken));
                var contract = await uow.GetConnection()!.ExecuteScalarAsync<Guid>(new CommandDefinition("SELECT contract_id FROM alert_notifications WHERE id = @Id", new { Id = notificationId }, uow.GetTransaction(), cancellationToken: cancellationToken));
                await AuditAsync("webhook_retried", actorId, contract, new { NotificationId = notificationId, request.OperationId }, response, cancellationToken);
                uow.Commit(); return Result.Success(response);
            }
            catch (Exception ex) { uow.Rollback(); return Result.Failure<AlertWebhookStatusView>(ex); }
        }
        private async Task AuditAsync(string action, string actor, Guid contract, object request, object response, CancellationToken cancellationToken)
        {
            var customer = await uow.GetConnection()!.ExecuteScalarAsync<string>(new CommandDefinition("SELECT customer_id FROM contracts WHERE id = @Id", new { Id = contract }, uow.GetTransaction(), cancellationToken: cancellationToken));
            var now = DateTimeOffset.UtcNow;
            (await audit.AddAsync(AuditLogEntry.Create("alert", action, actor, customer, contract, null, null, null,
                JsonSerializer.Serialize(new { Request = request, Response = response }), now, now.AddDays(90)), uow, cancellationToken)).EnsureSuccess();
        }
    }
}

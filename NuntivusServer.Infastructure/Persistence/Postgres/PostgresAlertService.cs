using System.Text.Json;
using Dapper;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Security;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Alerts;
using NuntivusServer.Domain.Auditing;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresAlertService(IDatabaseConnectionFactory connections, IContractSloService slos,
        IContractRepository contracts, IEndpointSecurityPolicy endpoints, IAuditLogRepository audit, IUnitOfWork uow) : IAlertService
    {
        internal const string RuleColumns = "id AS Id, contract_id AS ContractId, name AS Name, signal AS Signal, threshold AS Threshold, reminder_seconds AS ReminderSeconds, enabled AS Enabled, active AS Active, webhook_url AS WebhookUrl, webhook_secret_ref AS WebhookSecretRef, archived AS Archived";
        private const string NotificationColumns = "id AS Id, rule_id AS RuleId, contract_id AS ContractId, state AS State, message AS Message, created_at AS CreatedAt, acknowledged_at AS AcknowledgedAt, acknowledged_by AS AcknowledgedBy";
        public async Task<Result<AlertRuleView>> SaveRuleAsync(Guid id, AlertRuleRequest request, string actorId, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty || request.ContractId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200
                || request.Signal is not ("SloSuccess" or "SloLatency" or "DestinationFailures" or "DeadLetters")
                || request.Threshold < 0 || request.Threshold > 1000000000 || request.ReminderSeconds != 0 && request.ReminderSeconds is < 60 or > 604800
                || request.Signal.StartsWith("Slo", StringComparison.Ordinal) && request.Threshold != 0
                || request.Signal is "DestinationFailures" or "DeadLetters" && (request.Threshold < 1 || decimal.Truncate(request.Threshold) != request.Threshold))
                return Result.Failure<AlertRuleView>(Error.Validation("Provide a name, supported signal, bounded threshold and reminder interval 0 or 60–604800 seconds. SLO threshold is zero remaining budget; count thresholds are positive integers."));
            if (string.IsNullOrWhiteSpace(actorId)) return Result.Failure<AlertRuleView>(Error.Unauthorized("Operator identity is required."));
            if (request.WebhookUrl is not null)
            {
                if (request.WebhookUrl.Length > 2000 || !Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out var uri))
                    return Result.Failure<AlertRuleView>(Error.Validation("Webhook URL is invalid."));
                var safe = endpoints.ValidateEndpointUri(uri, new(EndpointDirection.Destination, "https", "Alerts"), new EndpointSecurityOption { AllowHttpScheme = false });
                if (safe.IsFailure) return Result<AlertRuleView>.Fail(safe.Errors);
                if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)) return Result.Failure<AlertRuleView>(Error.Validation("Webhook URLs cannot contain credentials or fragments."));
            }
            if (request.WebhookSecretRef is not null && (request.WebhookUrl is null || !request.WebhookSecretRef.StartsWith("secret.", StringComparison.Ordinal) || request.WebhookSecretRef.Length > 200))
                return Result.Failure<AlertRuleView>(Error.Validation("Webhook credentials must reference a managed secret and require a webhook URL."));
            var contract = await contracts.GetByIdAsync(request.ContractId, cancellationToken);
            if (contract.IsFailure) return Result<AlertRuleView>.Fail(contract.Errors);
            if (contract.Value is null) return Result.Failure<AlertRuleView>(Error.NotFound("Contract was not found."));
            try
            {
                uow.Begin(true);
                await uow.GetConnection()!.ExecuteAsync(new CommandDefinition(
                    "SELECT pg_advisory_xact_lock(hashtextextended(@Id, 0))", new { Id = id.ToString("D") }, uow.GetTransaction(), cancellationToken: cancellationToken));
                if (request.WebhookSecretRef is not null)
                {
                    var secret = await uow.GetConnection()!.ExecuteScalarAsync<string?>(new CommandDefinition(
                        "SELECT name FROM managed_secrets WHERE name = @Name FOR KEY SHARE", new { Name = request.WebhookSecretRef }, uow.GetTransaction(), cancellationToken: cancellationToken));
                    if (secret is null) { uow.Rollback(); return Result.Failure<AlertRuleView>(Error.Validation("Managed webhook secret does not exist.")); }
                }
                var previous = await uow.GetConnection()!.QuerySingleOrDefaultAsync<AlertRuleView>(new CommandDefinition(
                    $"SELECT {RuleColumns} FROM alert_rules WHERE id = @Id FOR UPDATE", new { Id = id }, uow.GetTransaction(), cancellationToken: cancellationToken));
                if (previous?.Archived == true)
                {
                    uow.Rollback();
                    return Result.Failure<AlertRuleView>(Error.Conflict("Archived rules cannot be modified; create a replacement rule."));
                }
                if (previous is not null && (previous.ContractId != request.ContractId || previous.Signal != request.Signal || previous.Threshold != request.Threshold))
                {
                    uow.Rollback();
                    return Result.Failure<AlertRuleView>(Error.Conflict("An existing rule's contract, signal and threshold are immutable; create a new rule to change them."));
                }
                var view = await uow.GetConnection()!.QuerySingleAsync<AlertRuleView>(new CommandDefinition($"""
                    INSERT INTO alert_rules (id, contract_id, name, signal, threshold, reminder_seconds, enabled, webhook_url, webhook_secret_ref, updated_at)
                    VALUES (@Id, @ContractId, @Name, @Signal, @Threshold, @ReminderSeconds, @Enabled, @WebhookUrl, @WebhookSecretRef, now())
                    ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, reminder_seconds = EXCLUDED.reminder_seconds,
                        enabled = EXCLUDED.enabled, webhook_url = EXCLUDED.webhook_url, webhook_secret_ref = EXCLUDED.webhook_secret_ref, updated_at = now()
                    RETURNING {RuleColumns}
                    """, new { Id = id, request.ContractId, Name = request.Name.Trim(), request.Signal, request.Threshold, request.ReminderSeconds, request.Enabled, request.WebhookUrl, request.WebhookSecretRef }, uow.GetTransaction(), cancellationToken: cancellationToken));
                (await audit.AddAsync(AuditLogEntry.Create("alert", "rule_saved", actorId, contract.Value.CustomerId, request.ContractId, null, null, null,
                    JsonSerializer.Serialize(new { Request = request, Response = view }), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90)), uow, cancellationToken)).EnsureSuccess();
                uow.Commit();
                return Result.Success(view);
            }
            catch (Exception ex) { uow.Rollback(); return Result.Failure<AlertRuleView>(ex); }
        }

        public async Task<Result<PagedResult<AlertRuleView>>> ListRulesAsync(int page, CancellationToken cancellationToken)
        {
            if (page < 1 || page > 1000000) return Result.Failure<PagedResult<AlertRuleView>>(Error.Validation("Invalid page."));
            try
            {
                using var connection = connections.CreateConnection();
                using var results = await connection.QueryMultipleAsync(new CommandDefinition($"SELECT {RuleColumns} FROM alert_rules ORDER BY updated_at DESC, id LIMIT 50 OFFSET @Offset; SELECT count(*) FROM alert_rules;", new { Offset = (page - 1) * 50 }, cancellationToken: cancellationToken));
                return Result.Success(PagedResult<AlertRuleView>.Create(await results.ReadAsync<AlertRuleView>(), page, 50, await results.ReadSingleAsync<long>()));
            }
            catch (Exception ex) { return Result.Failure<PagedResult<AlertRuleView>>(ex); }
        }
        public async Task<Result<PagedResult<AlertNotificationView>>> ListNotificationsAsync(int page, CancellationToken cancellationToken)
        {
            if (page < 1 || page > 1000000) return Result.Failure<PagedResult<AlertNotificationView>>(Error.Validation("Invalid page."));
            try
            {
                using var connection = connections.CreateConnection();
                using var results = await connection.QueryMultipleAsync(new CommandDefinition($"SELECT {NotificationColumns} FROM alert_notifications ORDER BY created_at DESC, id LIMIT 50 OFFSET @Offset; SELECT count(*) FROM alert_notifications;", new { Offset = (page - 1) * 50 }, cancellationToken: cancellationToken));
                var rows = await results.ReadAsync<NotificationRow>();
                return Result.Success(PagedResult<AlertNotificationView>.Create(rows.Select(row => row.ToView()), page, 50, await results.ReadSingleAsync<long>()));
            }
            catch (Exception ex) { return Result.Failure<PagedResult<AlertNotificationView>>(ex); }
        }
        public async Task<Result<AlertNotificationView>> AcknowledgeAsync(Guid id, string actorId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(actorId)) return Result.Failure<AlertNotificationView>(Error.Unauthorized("Operator identity is required."));
            try
            {
                uow.Begin(true);
                var result = await uow.GetConnection()!.QuerySingleOrDefaultAsync<NotificationRow>(new CommandDefinition($"""
                    UPDATE alert_notifications SET acknowledged_at = COALESCE(acknowledged_at, now()), acknowledged_by = COALESCE(acknowledged_by, @Actor)
                    WHERE id = @Id RETURNING {NotificationColumns}
                    """, new { Id = id, Actor = actorId }, uow.GetTransaction(), cancellationToken: cancellationToken));
                if (result is null) { uow.Rollback(); return Result.Failure<AlertNotificationView>(Error.NotFound("Notification was not found.")); }
                var response = result.ToView();
                var now = DateTimeOffset.UtcNow;
                (await audit.AddAsync(AuditLogEntry.Create("alert", "acknowledged", actorId, null, result.ContractId, null, null, null,
                    JsonSerializer.Serialize(new { Request = new { NotificationId = id }, Response = response }), now, now.AddDays(90)), uow, cancellationToken)).EnsureSuccess();
                uow.Commit();
                return Result.Success(response);
            }
            catch (Exception ex) { uow.Rollback(); return Result.Failure<AlertNotificationView>(ex); }
        }

        private sealed class NotificationRow
        {
            public Guid Id { get; set; }
            public Guid RuleId { get; set; }
            public Guid ContractId { get; set; }
            public string State { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset? AcknowledgedAt { get; set; }
            public string? AcknowledgedBy { get; set; }
            public AlertNotificationView ToView() => new(Id, RuleId, ContractId, State, Message, CreatedAt, AcknowledgedAt, AcknowledgedBy);
        }

        public async Task<Result<int>> EvaluateAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var connection = connections.CreateConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();
                var rules = (await connection.QueryAsync<AlertRuleView>(new CommandDefinition($"SELECT {RuleColumns} FROM alert_rules WHERE enabled ORDER BY COALESCE(last_evaluated_at, '-infinity'), id LIMIT 100 FOR UPDATE SKIP LOCKED", transaction: transaction, cancellationToken: cancellationToken))).ToArray();
                var emitted = 0;
                foreach (var rule in rules)
                {
                    await connection.ExecuteAsync(new CommandDefinition("UPDATE alert_rules SET last_evaluated_at = now() WHERE id = @Id", new { Id = rule.Id }, transaction, cancellationToken: cancellationToken));
                    decimal? value;
                    if (rule.Signal.StartsWith("Slo", StringComparison.Ordinal))
                    {
                        var slo = await slos.GetAsync(rule.ContractId, cancellationToken);
                        if (slo.IsFailure) continue;
                        var budget = rule.Signal == "SloSuccess" ? slo.Value.Success : slo.Value.Latency;
                        value = budget.Eligible == 0 ? null : budget.RemainingFailures < 0 ? 1 : 0;
                    }
                    else value = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(rule.Signal == "DeadLetters"
                        ? "SELECT count(*) FROM deliveries WHERE contract_id = @Id AND status = 'Dead'"
                        : "SELECT COALESCE((SELECT max(consecutive_failures) FROM destination_failure_counters WHERE contract_id = @Id), (SELECT consecutive_failures FROM destination_health WHERE contract_id = @Id), 0)",
                        new { Id = rule.ContractId }, transaction, cancellationToken: cancellationToken));
                    if (value is null) continue;
                    var firing = rule.Signal.StartsWith("Slo", StringComparison.Ordinal) ? value > 0 : value >= rule.Threshold;
                    var due = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT last_notified_at IS NULL OR (@Reminder > 0 AND last_notified_at <= now() - make_interval(secs => @Reminder)) FROM alert_rules WHERE id = @Id", new { Id = rule.Id, Reminder = rule.ReminderSeconds }, transaction, cancellationToken: cancellationToken));
                    if (firing == rule.Active && (!firing || !due)) continue;
                    var id = Guid.NewGuid();
                    var state = !firing ? "Recovered" : rule.Active ? "Reminder" : "Firing";
                    var message = $"{rule.Name}: {state} ({rule.Signal}).";
                    var payload = JsonSerializer.Serialize(new { id, ruleId = rule.Id, contractId = rule.ContractId, state, message });
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO alert_notifications (id, rule_id, contract_id, state, message, created_at) VALUES (@Id, @RuleId, @ContractId, @State, @Message, now());
                        INSERT INTO alert_webhook_outbox (notification_id, endpoint, secret_ref, payload, next_attempt_at)
                        SELECT @Id, webhook_url, webhook_secret_ref, @Payload::jsonb, now() FROM alert_rules WHERE id = @RuleId AND webhook_url IS NOT NULL;
                        UPDATE alert_rules SET active = @Firing, last_notified_at = now() WHERE id = @RuleId;
                        """, new { Id = id, RuleId = rule.Id, rule.ContractId, State = state, Message = message, Payload = payload, Firing = firing }, transaction, cancellationToken: cancellationToken));
                    emitted++;
                }
                transaction.Commit();
                return Result.Success(emitted);
            }
            catch (Exception ex) { return Result.Failure<int>(ex); }
        }
    }
}

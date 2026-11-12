# Alert rules and notifications

The administrative **Alerts** page provides a paged in-app inbox, acknowledgement, rule creation, and enable/disable controls. Viewer can read; Operator can configure and acknowledge. Actors come from the authenticated principal. Rule changes and acknowledgement request/response snapshots are audited transactionally. Repeated acknowledgement returns 200 with the original acknowledgement identity/time.

## Signals

Rules belong to a contract and use one of:

- `SloSuccess` or `SloLatency`: fires when the corresponding rolling SLO remaining failure budget is negative. Threshold must be 0. No eligible traffic or an unavailable policy does not assert either failure or recovery; the prior alert state is retained.
- `DestinationFailures`: fires when any destination binding's persisted consecutive failure count reaches the positive integer threshold. A success resets only that binding's counter. Migration `016_destination_failure_counters.sql` adds these counters; historical aggregate health is used only until the first new observation for a contract. Binding-specific history is not reconstructed. The existing contract-level health summary remains an aggregate view, not the alert counter.
- `DeadLetters`: fires when the current number of Dead deliveries reaches the positive integer threshold. Requeue, cancellation, successful recovery and ordinary delivery retention can reduce this operational backlog count. It is not a historical reliability denominator; use SLOs for that purpose.

An initially healthy rule emits nothing. Transition to unhealthy emits `Firing`; return to healthy emits `Recovered`. Unchanged firing state emits `Reminder` only when `reminderSeconds` is nonzero and the interval has elapsed. Supported reminder values are 0 (off) or 60–604800 seconds. Acknowledgement does not mute the rule or stop reminders. Disabling pauses evaluation and preserves state, inbox entries and already queued webhook messages. Re-enabling resumes from that state.

Contract, signal and threshold are immutable for an existing rule ID. Name, reminder interval, enabled state and webhook configuration can be updated through PUT; create a new rule ID for a different obligation. API pages contain at most 50 entries.

Operators can archive an obsolete rule using the Rules action. Archiving permanently disables evaluation and prevents edits/re-enabling, but retains notifications, attempt history and queued sends. Repeating archive returns 200 with its current state. Archived rules remain labeled in the paged list. To replace immutable settings, create a replacement rule first, then archive the old one; the UI explains this sequence.

## API

- `GET /v1/alerts/rules?page=1`
- `PUT /v1/alerts/rules/{id}` with `contractId`, `name`, `signal`, `threshold`, `reminderSeconds`, `enabled`, optional `webhookUrl` and `webhookSecretRef`
- `GET /v1/alerts/notifications?page=1`
- `POST /v1/alerts/notifications/{id}/acknowledge`
- `DELETE /v1/alerts/rules/{id}` archives the rule (Operator)
- `GET /v1/alerts/notifications/{id}/webhook` returns safe status and lifetime/current-cycle counts (Viewer)
- `GET /v1/alerts/notifications/{id}/webhook/attempts?page=1` returns up to 50 attempt outcomes (Viewer)
- `POST /v1/alerts/notifications/{id}/webhook/retry` with `operationId` UUID (Operator)

Migration `014_alert_notifications.sql` creates explicit rule state, notifications and an independent durable webhook outbox. Worker evaluation runs approximately every 30 seconds in fair batches of at most 100 rules. A separate webhook BackgroundService handles network sends so slow receivers do not delay evaluation. Row locking and atomic state/inbox/outbox writes prevent duplicate transitions across workers. More than 100 rules require additional cycles; this is not an instantaneous paging guarantee. Notification and webhook history is currently retained until its owning contract is deleted; provision storage accordingly.

## Outbound webhooks

Webhook delivery is independent from customer event deliveries: its failures never generate delivery failures or recursively trigger these rules. An optional managed secret reference supplies a Bearer token. References must exist and are protected against deletion while referenced by rules or incomplete outbox messages. Credentials are resolved when sending, allowing rotation; secret values are not stored in notification payloads or error text.

Endpoints must use HTTPS without URL credentials/fragments. Redirects and proxies are disabled. DNS addresses are validated at connection time against the shared private-network policy, and the socket connects to a validated address. Requests time out after 15 seconds. Only 2xx is success; response bodies are not retained.

Each notification snapshots its endpoint, secret reference and JSON payload (`id`, `ruleId`, `contractId`, `state`, `message`). The stable `X-Nuntivus-Notification-Id` header supports receiver deduplication. Delivery is at least once: a crash after a receiver accepts but before local completion may resend. Leases expire after one minute; stale lease completions cannot overwrite a newer claim. At most ten attempts are made per retry cycle, including expired claims, with bounded exponential delay (up to one hour). A terminal failure retains a generic error in the outbox; it does not erase the in-app notification. Up to 20 messages are handled per worker cycle.

Migration `017_alert_operations.sql` adds archive state, durable attempt outcomes and idempotent retry operations. Open **Webhook delivery** on a notification to inspect Pending, Sending, Failed, Exhausted, Succeeded or NotConfigured status. Attempt history includes timestamps and generic Failed/Succeeded/LeaseExpired outcomes, never credentials, payloads, response bodies or arbitrary remote errors. Historical lifetime counts remain intact; details predating this migration are not reconstructed.

Manual retry is allowed for any failed delivery with no active lease, including automatic-backoff failures and exhausted/expired sends. Pending unattempted and successful deliveries cannot be retried. Retry queues the original snapshot immediately with a fresh ten-attempt budget and preserves lifetime attempt count/history. The receiver may already have processed a failed/expired send; deduplicate using the unchanged notification ID. Repeating the same notification/operation ID returns 200 with current status without scheduling another cycle. Use a new operation ID for a later intentional retry. Archive/retry audit records contain the principal-derived actor and full operation request/response; they commit atomically with the mutation.

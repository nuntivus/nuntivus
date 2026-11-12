ALTER TABLE alert_rules ADD COLUMN archived boolean NOT NULL DEFAULT false;
ALTER TABLE alert_rules ADD CONSTRAINT ck_alert_rules_archived_disabled CHECK (NOT archived OR NOT enabled);
ALTER TABLE alert_webhook_outbox ADD COLUMN retry_budget_offset integer NOT NULL DEFAULT 0;
ALTER TABLE alert_webhook_outbox ADD CONSTRAINT ck_alert_webhook_retry_budget CHECK (retry_budget_offset >= 0 AND retry_budget_offset <= attempt_count);

CREATE TABLE alert_webhook_attempts (
	lease_token uuid PRIMARY KEY,
	notification_id uuid NOT NULL REFERENCES alert_webhook_outbox(notification_id) ON DELETE CASCADE,
	attempt_number integer NOT NULL,
	started_at timestamptz NOT NULL,
	completed_at timestamptz NULL,
	outcome text NOT NULL CHECK (outcome IN ('InFlight', 'Succeeded', 'Failed', 'LeaseExpired')),
	error text NULL,
	UNIQUE (notification_id, attempt_number)
);
CREATE INDEX ix_alert_webhook_attempts_notification ON alert_webhook_attempts(notification_id, attempt_number DESC);
CREATE TABLE alert_webhook_retry_operations (
	notification_id uuid NOT NULL REFERENCES alert_webhook_outbox(notification_id) ON DELETE CASCADE,
	operation_id uuid NOT NULL,
	requested_at timestamptz NOT NULL,
	requested_by text NOT NULL,
	PRIMARY KEY (notification_id, operation_id)
);

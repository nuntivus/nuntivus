CREATE TABLE alert_rules (
	id uuid PRIMARY KEY,
	contract_id uuid NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	name text NOT NULL,
	signal text NOT NULL CHECK (signal IN ('SloSuccess', 'SloLatency', 'DestinationFailures', 'DeadLetters')),
	threshold numeric NOT NULL CHECK (threshold >= 0),
	reminder_seconds integer NOT NULL CHECK (reminder_seconds = 0 OR reminder_seconds BETWEEN 60 AND 604800),
	enabled boolean NOT NULL,
	webhook_url text NULL,
	webhook_secret_ref text NULL,
	active boolean NOT NULL DEFAULT false,
	last_notified_at timestamptz NULL,
	last_evaluated_at timestamptz NULL,
	updated_at timestamptz NOT NULL,
	CHECK (webhook_secret_ref IS NULL OR webhook_url IS NOT NULL)
);
CREATE INDEX ix_alert_rules_enabled ON alert_rules (id) WHERE enabled;
CREATE TABLE alert_notifications (
	id uuid PRIMARY KEY,
	rule_id uuid NOT NULL REFERENCES alert_rules(id) ON DELETE CASCADE,
	contract_id uuid NOT NULL,
	state text NOT NULL CHECK (state IN ('Firing', 'Reminder', 'Recovered')),
	message text NOT NULL,
	created_at timestamptz NOT NULL,
	acknowledged_at timestamptz NULL,
	acknowledged_by text NULL
);
CREATE INDEX ix_alert_notifications_created ON alert_notifications (created_at DESC, id);
CREATE TABLE alert_webhook_outbox (
	notification_id uuid PRIMARY KEY REFERENCES alert_notifications(id) ON DELETE CASCADE,
	endpoint text NOT NULL,
	secret_ref text NULL,
	payload jsonb NOT NULL,
	attempt_count integer NOT NULL DEFAULT 0,
	next_attempt_at timestamptz NOT NULL,
	lease_token uuid NULL,
	leased_until timestamptz NULL,
	completed_at timestamptz NULL,
	last_error text NULL
);
CREATE INDEX ix_alert_webhook_outbox_due ON alert_webhook_outbox (next_attempt_at) WHERE completed_at IS NULL;

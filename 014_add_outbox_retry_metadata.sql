ALTER TABLE outbox_messages
	ADD COLUMN next_attempt_at timestamptz NULL;

ALTER TABLE outbox_messages
	ADD COLUMN last_failed_at timestamptz NULL;

ALTER TABLE outbox_messages
	ADD COLUMN last_error text NULL;

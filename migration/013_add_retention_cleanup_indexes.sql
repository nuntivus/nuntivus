CREATE INDEX IF NOT EXISTS ix_outbox_messages_processed_at
	ON outbox_messages(processed_at)
	WHERE processed_at IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_delivery_attempts_completed_at
	ON delivery_attempts(completed_at);

CREATE INDEX IF NOT EXISTS ix_deliveries_terminal_updated_at
	ON deliveries(updated_at, id)
	WHERE status IN ('Delivered', 'Dead')
	   OR (status = 'Failed' AND next_attempt_at IS NULL);

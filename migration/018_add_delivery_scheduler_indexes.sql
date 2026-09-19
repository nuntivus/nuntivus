CREATE INDEX IF NOT EXISTS ix_deliveries_scheduler_due
	ON deliveries(status, next_attempt_at, created_at, id)
	WHERE next_attempt_at IS NOT NULL
	  AND status IN ('Pending', 'Failed');

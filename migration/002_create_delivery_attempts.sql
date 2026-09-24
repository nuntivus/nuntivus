CREATE TABLE IF NOT EXISTS delivery_attempts
(
	id                     uuid        PRIMARY KEY,
	delivery_id            uuid        NOT NULL REFERENCES deliveries(id) ON DELETE CASCADE,
	attempt_number         int         NOT NULL,
	succeeded              boolean     NOT NULL,
	failure_category       text        NULL,
	error                  text        NULL,
	metadata               jsonb       NOT NULL DEFAULT '{}'::jsonb,
	started_at             timestamptz NOT NULL,
	completed_at           timestamptz NOT NULL,
	CONSTRAINT uq_delivery_attempts_delivery_attempt UNIQUE (delivery_id, attempt_number)
);

CREATE INDEX IF NOT EXISTS ix_delivery_attempts_delivery_id
	ON delivery_attempts(delivery_id);

CREATE INDEX IF NOT EXISTS ix_delivery_attempts_completed_at
	ON delivery_attempts(completed_at);

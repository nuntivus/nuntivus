CREATE TABLE delivery_replay_jobs
(
	id               uuid        PRIMARY KEY,
	operation_id     uuid        NOT NULL,
	operator_id      text        NOT NULL,
	request_snapshot jsonb       NOT NULL,
	status           text        NOT NULL,
	event_type       text        NULL,
	customer_id      text        NULL,
	contract_id      uuid        NULL REFERENCES contracts(id) ON DELETE SET NULL,
	created_from     timestamptz NULL,
	created_to       timestamptz NULL,
	batch_limit      int         NOT NULL,
	replayed_count   int         NOT NULL DEFAULT 0,
	error            text        NULL,
	created_at       timestamptz NOT NULL,
	updated_at       timestamptz NOT NULL,
	completed_at     timestamptz NULL,
	CONSTRAINT uq_delivery_replay_jobs_operation UNIQUE (operation_id),
	CONSTRAINT ck_delivery_replay_jobs_batch_limit_positive CHECK (batch_limit > 0),
	CONSTRAINT ck_delivery_replay_jobs_replayed_count_non_negative CHECK (replayed_count >= 0)
);

CREATE INDEX ix_delivery_replay_jobs_status_created
	ON delivery_replay_jobs(status, created_at);

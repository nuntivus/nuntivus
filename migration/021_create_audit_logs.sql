CREATE TABLE audit_logs
(
	id           uuid        PRIMARY KEY,
	category     text        NOT NULL,
	action       text        NOT NULL,
	actor_id     text        NULL,
	customer_id  text        NULL,
	contract_id  uuid        NULL,
	delivery_id  uuid        NULL,
	event_id     uuid        NULL,
	operation_id uuid        NULL,
	details      jsonb       NOT NULL,
	created_at   timestamptz NOT NULL,
	expires_at   timestamptz NOT NULL
);

CREATE INDEX ix_audit_logs_expires_at
	ON audit_logs(expires_at);

CREATE INDEX ix_audit_logs_created_at
	ON audit_logs(created_at DESC);

CREATE INDEX ix_audit_logs_customer_created
	ON audit_logs(customer_id, created_at DESC)
	WHERE customer_id IS NOT NULL;

CREATE INDEX ix_audit_logs_contract_created
	ON audit_logs(contract_id, created_at DESC)
	WHERE contract_id IS NOT NULL;

CREATE INDEX ix_audit_logs_delivery_created
	ON audit_logs(delivery_id, created_at DESC)
	WHERE delivery_id IS NOT NULL;

CREATE INDEX ix_audit_logs_event_created
	ON audit_logs(event_id, created_at DESC)
	WHERE event_id IS NOT NULL;

CREATE INDEX ix_audit_logs_operation
	ON audit_logs(operation_id)
	WHERE operation_id IS NOT NULL;
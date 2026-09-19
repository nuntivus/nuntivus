CREATE TABLE delivery_operation_audits
(
	id                uuid        PRIMARY KEY,
	delivery_id       uuid        NOT NULL REFERENCES deliveries(id) ON DELETE CASCADE,
	operation_id      uuid        NOT NULL,
	operation         text        NOT NULL,
	operator_id       text        NOT NULL,
	request_snapshot  jsonb       NOT NULL,
	response_snapshot jsonb       NOT NULL,
	created_at        timestamptz NOT NULL,
	CONSTRAINT uq_delivery_operation_audits_delivery_operation UNIQUE (delivery_id, operation_id)
);

CREATE INDEX ix_delivery_operation_audits_delivery_created
	ON delivery_operation_audits(delivery_id, created_at DESC);

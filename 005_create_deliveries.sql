CREATE TABLE deliveries
(
	id            uuid        PRIMARY KEY,
	event_id      uuid        NOT NULL REFERENCES events(id)    ON DELETE CASCADE,
	contract_id   uuid        NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	customer_id   text        NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
	event_type    text        NOT NULL,
	payload       jsonb       NOT NULL,
	status        text        NOT NULL,
	created_at    timestamptz NOT NULL,
	updated_at    timestamptz NOT NULL,
	CONSTRAINT uq_deliveries_event_contract UNIQUE (event_id, contract_id)
);

CREATE INDEX ix_deliveries_status_created_at
	ON deliveries(status, created_at);

CREATE INDEX ix_deliveries_contract_id
	ON deliveries(contract_id);

CREATE INDEX ix_deliveries_customer_id
	ON deliveries(customer_id);

CREATE INDEX ix_deliveries_event_id
	ON deliveries(event_id);

CREATE TABLE contracts
(
	id                    uuid        PRIMARY KEY,
	customer_id           text        NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
	enabled               boolean     NOT NULL DEFAULT TRUE,
	destination_type      text        NOT NULL,
	destination_config    jsonb       NOT NULL,
	created_at            timestamptz NOT NULL,
	updated_at            timestamptz NOT NULL
);

CREATE INDEX ix_contracts_customer_id
	ON contracts(customer_id);

CREATE INDEX ix_contracts_customer_enabled
	ON contracts(customer_id, enabled);

CREATE INDEX ix_contracts_created_at
	ON contracts(created_at DESC);


CREATE TABLE contract_event_types
(
	contract_id  uuid NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	event_type   text NOT NULL,
	PRIMARY KEY (contract_id, event_type)
);

CREATE INDEX ix_contract_event_types_event_type
	ON contract_event_types(event_type);

CREATE TABLE contract_schema_targets
(
	contract_id    uuid    NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	event_type     text    NOT NULL,
	schema_version integer NOT NULL,
	PRIMARY KEY (contract_id, event_type),
	CONSTRAINT ck_contract_schema_targets_schema_version CHECK (schema_version > 0)
);

CREATE INDEX ix_contract_schema_targets_contract_id
	ON contract_schema_targets(contract_id);

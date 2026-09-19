ALTER TABLE contracts
	ADD COLUMN IF NOT EXISTS ordering_enabled boolean NOT NULL DEFAULT FALSE,
	ADD COLUMN IF NOT EXISTS ordering_key_selector text NULL;

ALTER TABLE deliveries
	ADD COLUMN IF NOT EXISTS ordering_enabled boolean NOT NULL DEFAULT FALSE,
	ADD COLUMN IF NOT EXISTS ordering_key text NULL;

CREATE INDEX IF NOT EXISTS ix_deliveries_ordering_key_created_at
	ON deliveries(ordering_key, created_at, id)
	WHERE ordering_enabled = TRUE;

CREATE TABLE IF NOT EXISTS contract_delivery_ordering_state
(
	ordering_key       text        PRIMARY KEY,
	active_delivery_id uuid        NULL REFERENCES deliveries(id) ON DELETE SET NULL,
	lease_expires_at   timestamptz NULL,
	updated_at         timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_contract_delivery_ordering_state_lease_expires_at
	ON contract_delivery_ordering_state(lease_expires_at);

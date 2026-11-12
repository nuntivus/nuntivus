CREATE TABLE destination_failure_counters (
	contract_id uuid NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	destination_binding_id uuid NULL,
	consecutive_failures integer NOT NULL CHECK (consecutive_failures >= 0),
	updated_at timestamptz NOT NULL,
	UNIQUE NULLS NOT DISTINCT (contract_id, destination_binding_id)
);

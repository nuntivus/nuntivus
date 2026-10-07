CREATE TABLE destinations
(
	id uuid PRIMARY KEY,
	name text NOT NULL,
	description text NULL,
	type text NOT NULL,
	configuration jsonb NOT NULL,
	enabled boolean NOT NULL DEFAULT TRUE,
	disable_policy text NOT NULL DEFAULT 'continue',
	created_at timestamptz NOT NULL,
	updated_at timestamptz NOT NULL,
	CONSTRAINT ck_destinations_disable_policy CHECK (disable_policy IN ('continue', 'pause')),
	CONSTRAINT ck_destinations_configuration_object CHECK (jsonb_typeof(configuration) = 'object')
);

CREATE TABLE contract_destinations
(
	id uuid PRIMARY KEY,
	contract_id uuid NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	destination_id uuid NULL REFERENCES destinations(id) ON DELETE SET NULL,
	destination_type text NOT NULL,
	destination_config jsonb NOT NULL,
	auth_ref text NULL,
	auth_version integer NULL,
	delivery_enabled boolean NOT NULL DEFAULT TRUE,
	created_at timestamptz NOT NULL,
	CONSTRAINT uq_contract_destinations_reference UNIQUE (contract_id, destination_id),
	CONSTRAINT ck_contract_destinations_config_object CHECK (jsonb_typeof(destination_config) = 'object')
);

CREATE INDEX ix_contract_destinations_destination_id ON contract_destinations(destination_id);
CREATE INDEX ix_contract_destinations_contract_id ON contract_destinations(contract_id);

-- Existing contract configuration becomes a detached snapshot; no registry entry is required.
INSERT INTO contract_destinations (id, contract_id, destination_type, destination_config, auth_ref, auth_version, created_at)
SELECT gen_random_uuid(), id, destination_type, destination_config, auth_ref, auth_version, created_at
FROM contracts;

ALTER TABLE deliveries ADD COLUMN destination_binding_id uuid NULL;
UPDATE deliveries d SET destination_binding_id = cd.id
FROM contract_destinations cd WHERE cd.contract_id = d.contract_id;
ALTER TABLE deliveries DROP CONSTRAINT uq_deliveries_event_contract;
ALTER TABLE deliveries ADD CONSTRAINT uq_deliveries_event_contract_binding
	UNIQUE NULLS NOT DISTINCT (event_id, contract_id, destination_binding_id);
CREATE INDEX ix_deliveries_destination_binding_id ON deliveries(destination_binding_id);

CREATE TABLE contract_sources
(
	contract_id uuid NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	source_id uuid NOT NULL REFERENCES sources(id) ON DELETE RESTRICT,
	PRIMARY KEY (contract_id, source_id)
);

CREATE INDEX ix_contract_sources_source_id ON contract_sources(source_id);

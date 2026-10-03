CREATE TABLE source_ingestion_leases
(
	source_id uuid PRIMARY KEY REFERENCES sources(id) ON DELETE CASCADE,
	owner_id text NOT NULL,
	epoch bigint NOT NULL DEFAULT 1,
	expires_at timestamptz NOT NULL
);

CREATE TABLE source_ingestion_progress
(
	source_id uuid NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
	item_key text NOT NULL,
	processed_at timestamptz NOT NULL DEFAULT now(),
	PRIMARY KEY (source_id, item_key)
);

CREATE INDEX ix_source_ingestion_leases_expires_at ON source_ingestion_leases(expires_at);

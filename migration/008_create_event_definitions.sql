CREATE TABLE event_definitions
(
	event_type   text        PRIMARY KEY,
	display_name text        NOT NULL,
	description  text        NULL,
	created_at   timestamptz NOT NULL
);

CREATE INDEX ix_event_definitions_created_at
	ON event_definitions(created_at DESC);

CREATE TABLE event_schema_versions
(
	event_type         text        NOT NULL REFERENCES event_definitions(event_type) ON DELETE CASCADE,
	version_number     integer     NOT NULL,
	schema_json        jsonb       NOT NULL,
	compatibility_mode text        NOT NULL,
	state_code         text        NOT NULL,
	created_at         timestamptz NOT NULL,
	activated_at       timestamptz NULL,
	deprecated_at      timestamptz NULL,
	PRIMARY KEY(event_type, version_number),
	CONSTRAINT ck_event_schema_versions_compatibility_mode
		CHECK (compatibility_mode IN ('Backward', 'Forward')),
	CONSTRAINT ck_event_schema_versions_state
		CHECK (state_code IN ('Draft', 'Active', 'Deprecated'))
);

CREATE INDEX ix_event_schema_versions_event_type_state
	ON event_schema_versions(event_type, state_code);

CREATE INDEX ix_event_schema_versions_created_at
	ON event_schema_versions(created_at DESC);

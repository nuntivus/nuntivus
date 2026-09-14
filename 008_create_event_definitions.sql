CREATE TABLE event_definitions
(
	event_type   text        PRIMARY KEY,
	display_name text        NOT NULL,
	description  text        NULL,
	created_at   timestamptz NOT NULL
);

CREATE INDEX ix_event_definitions_created_at
	ON event_definitions(created_at DESC);
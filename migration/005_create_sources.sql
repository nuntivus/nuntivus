CREATE TABLE sources
(
	id            uuid        PRIMARY KEY,
	name          text        NOT NULL,
	description   text        NULL,
	type          text        NOT NULL,
	enabled       boolean     NOT NULL DEFAULT TRUE,
	configuration jsonb       NOT NULL,
	created_at    timestamptz NOT NULL,
	updated_at    timestamptz NOT NULL
);

CREATE INDEX ix_sources_enabled_type ON sources(type, enabled);
CREATE INDEX ix_sources_created_at ON sources(created_at DESC);

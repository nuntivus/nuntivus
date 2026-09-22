CREATE TABLE instance_settings
(
	key        text        PRIMARY KEY,
	value      jsonb       NOT NULL,
	updated_at timestamptz NOT NULL,
	updated_by text        NULL
);

CREATE TABLE user_settings
(
	subject    text        NOT NULL,
	key        text        NOT NULL,
	value      jsonb       NOT NULL,
	updated_at timestamptz NOT NULL,
	PRIMARY KEY (subject, key)
);

CREATE INDEX ix_user_settings_subject_updated_at
	ON user_settings(subject, updated_at DESC);
CREATE TABLE managed_secret_versions (
	name text NOT NULL REFERENCES managed_secrets(name) ON DELETE CASCADE,
	version integer NOT NULL CHECK (version > 0),
	encrypted_value text NOT NULL,
	created_at timestamptz NOT NULL DEFAULT now(),
	PRIMARY KEY (name, version)
);

INSERT INTO managed_secret_versions (name, version, encrypted_value, created_at)
SELECT name, 1, encrypted_value, created_at FROM managed_secrets;

ALTER TABLE managed_secrets DROP COLUMN encrypted_value;

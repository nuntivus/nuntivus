CREATE TABLE IF NOT EXISTS managed_secrets (
	name text PRIMARY KEY,
	encrypted_value text NOT NULL,
	created_at timestamptz NOT NULL DEFAULT now()
);

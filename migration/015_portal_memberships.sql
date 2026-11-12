CREATE TABLE portal_accounts (
	id uuid PRIMARY KEY,
	issuer text NOT NULL,
	subject text NOT NULL,
	password_hash text NULL,
	credential_version bigint NOT NULL DEFAULT 1 CHECK (credential_version > 0),
	disabled boolean NOT NULL DEFAULT false,
	created_at timestamptz NOT NULL,
	UNIQUE (issuer, subject)
);
CREATE TABLE portal_memberships (
	account_id uuid NOT NULL REFERENCES portal_accounts(id) ON DELETE CASCADE,
	customer_id text NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
	revoked_at timestamptz NULL,
	created_at timestamptz NOT NULL,
	PRIMARY KEY (account_id, customer_id)
);
CREATE TABLE portal_invitations (
	id uuid PRIMARY KEY,
	customer_id text NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
	issuer text NOT NULL,
	subject text NOT NULL,
	token_hash text NOT NULL UNIQUE,
	expires_at timestamptz NOT NULL,
	redeemed_at timestamptz NULL,
	revoked_at timestamptz NULL,
	created_at timestamptz NOT NULL
);
CREATE INDEX ix_portal_memberships_customer ON portal_memberships(customer_id);
CREATE INDEX ix_portal_invitations_customer ON portal_invitations(customer_id);

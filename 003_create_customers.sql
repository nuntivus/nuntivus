CREATE TABLE customers
(
	id          text        PRIMARY KEY,
	name        text        NOT NULL,
	enabled     boolean     NOT NULL DEFAULT TRUE,
	created_at  timestamptz NOT NULL,
	updated_at  timestamptz NOT NULL
);

CREATE INDEX ix_customers_enabled
	ON customers(enabled);

CREATE INDEX ix_customers_created_at
	ON customers(created_at DESC);

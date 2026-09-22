CREATE TABLE admin_users
(
	subject      text        PRIMARY KEY,
	display_name text        NULL,
	created_at   timestamptz NOT NULL,
	updated_at   timestamptz NOT NULL
);

CREATE TABLE admin_user_roles
(
	subject text NOT NULL,
	role    text NOT NULL,
	created_at timestamptz NOT NULL,
	PRIMARY KEY (subject, role),
	CONSTRAINT fk_admin_user_roles_users
		FOREIGN KEY (subject) REFERENCES admin_users(subject)
		ON DELETE CASCADE
);

CREATE INDEX ix_admin_user_roles_role
	ON admin_user_roles(role);

CREATE INDEX ix_admin_users_updated_at
	ON admin_users(updated_at DESC);
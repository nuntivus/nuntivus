-- Consolidated migration script generated from individual migration files.
-- NOTE: This hard-replace baseline assumes a fresh/recreated database.

-- =============================================
-- Source: 001_create_events.sql
-- =============================================
CREATE TABLE events
(
    id          uuid        PRIMARY KEY,
    type        text        NOT NULL,
    idempotency_key  text   NOT NULL,
    source_id   uuid        NOT NULL,
    occurred_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL,
    metadata    jsonb       NOT NULL DEFAULT '{}'::jsonb,
    payload     jsonb       NOT NULL,
    CONSTRAINT uq_events_source_idempotency UNIQUE (source_id, idempotency_key)
);

CREATE INDEX ix_events_type
    ON events(type);

CREATE INDEX ix_events_idempotency_key
    ON events(idempotency_key);

CREATE INDEX ix_events_source_id
    ON events(source_id);

CREATE INDEX ix_events_received_at
    ON events(received_at);


-- =============================================
-- Source: 002_create_outbox_messages.sql
-- =============================================
CREATE TABLE outbox_messages
(
    id            uuid        PRIMARY KEY,
    event_id      uuid        NOT NULL,
    type          text        NOT NULL,
    payload       jsonb       NOT NULL,
    created_at    timestamptz NOT NULL,
    processed_at  timestamptz NULL,
    attempt_count integer     NOT NULL DEFAULT 0,
    claimed_by    text        NULL,
    claim_expires_at timestamptz NULL,
    CONSTRAINT fk_outbox_messages_event
        FOREIGN KEY (event_id)
        REFERENCES events (id)
        ON DELETE CASCADE
);

CREATE INDEX ix_outbox_messages_event_id
    ON outbox_messages(event_id);

CREATE INDEX ix_outbox_messages_pending
    ON outbox_messages(created_at)
    WHERE processed_at IS NULL;


-- =============================================
-- Source: 003_create_customers.sql
-- =============================================
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


-- =============================================
-- Source: 004_create_contracts.sql
-- =============================================
CREATE TABLE contracts
(
	id                    uuid        PRIMARY KEY,
	customer_id           text        NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
	enabled               boolean     NOT NULL DEFAULT TRUE,
	destination_type      text        NOT NULL,
	destination_config    jsonb       NOT NULL,
	created_at            timestamptz NOT NULL,
	updated_at            timestamptz NOT NULL
);

CREATE INDEX ix_contracts_customer_id
	ON contracts(customer_id);

CREATE INDEX ix_contracts_customer_enabled
	ON contracts(customer_id, enabled);

CREATE INDEX ix_contracts_created_at
	ON contracts(created_at DESC);


CREATE TABLE contract_event_types
(
	contract_id  uuid NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	event_type   text NOT NULL,
	PRIMARY KEY (contract_id, event_type)
);

CREATE INDEX ix_contract_event_types_event_type
	ON contract_event_types(event_type);

CREATE TABLE contract_schema_targets
(
	contract_id    uuid    NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	event_type     text    NOT NULL,
	schema_version integer NOT NULL,
	PRIMARY KEY (contract_id, event_type),
	CONSTRAINT ck_contract_schema_targets_schema_version CHECK (schema_version > 0)
);

CREATE INDEX ix_contract_schema_targets_contract_id
	ON contract_schema_targets(contract_id);


-- =============================================
-- Source: 005_create_deliveries.sql
-- =============================================
CREATE TABLE deliveries
(
	id                     uuid        PRIMARY KEY,
	event_id               uuid        NOT NULL REFERENCES events(id)    ON DELETE CASCADE,
	contract_id            uuid        NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	customer_id            text        NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
	event_type             text        NOT NULL,
	payload                jsonb       NOT NULL,
	destination_type       text        NOT NULL,
	destination_config     jsonb       NOT NULL,
	status                 text        NOT NULL,
	attempt_count          int         NOT NULL DEFAULT 0,
	last_error             text        NULL,
	next_attempt_at        timestamptz NULL,
	created_at             timestamptz NOT NULL,
	updated_at             timestamptz NOT NULL,
	CONSTRAINT uq_deliveries_event_contract UNIQUE (event_id, contract_id)
);

CREATE INDEX ix_deliveries_status_created_at
	ON deliveries(status, created_at);

CREATE INDEX ix_deliveries_contract_id
	ON deliveries(contract_id);

CREATE INDEX ix_deliveries_customer_id
	ON deliveries(customer_id);

CREATE INDEX ix_deliveries_event_id
	ON deliveries(event_id);


-- =============================================
-- Source: 007_create_event_replays.sql
-- =============================================
CREATE TABLE event_replays
(
	source_event_id   uuid        NOT NULL REFERENCES events(id) ON DELETE CASCADE,
	replay_key        text        NOT NULL,
	replayed_event_id uuid        NOT NULL REFERENCES events(id) ON DELETE CASCADE,
	created_at        timestamptz NOT NULL,
	CONSTRAINT pk_event_replays PRIMARY KEY (source_event_id, replay_key),
	CONSTRAINT uq_event_replays_replayed_event UNIQUE (replayed_event_id)
);

CREATE INDEX ix_event_replays_replayed_event_id
	ON event_replays(replayed_event_id);

CREATE INDEX ix_event_replays_created_at
	ON event_replays(created_at);


-- =============================================
-- Source: 008_create_event_definitions.sql
-- =============================================
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


-- =============================================
-- Source: 009_create_contract_filters.sql
-- =============================================
CREATE TABLE contract_filter_rules
(
	contract_id uuid    NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	scope       text    NOT NULL,
	rule_order  integer NOT NULL,
	selector    text    NOT NULL,
	operator    text    NOT NULL,
	value       text    NULL,
	values_json jsonb   NOT NULL DEFAULT '[]'::jsonb,
	PRIMARY KEY (contract_id, scope, rule_order),
	CONSTRAINT ck_contract_filter_rules_scope CHECK (scope IN ('metadata', 'payload')),
	CONSTRAINT ck_contract_filter_rules_rule_order CHECK (rule_order >= 0),
	CONSTRAINT ck_contract_filter_rules_selector_not_empty CHECK (char_length(trim(selector)) > 0),
	CONSTRAINT ck_contract_filter_rules_operator_not_empty CHECK (char_length(trim(operator)) > 0),
	CONSTRAINT ck_contract_filter_rules_values_json_is_array CHECK (jsonb_typeof(values_json) = 'array')
);

CREATE INDEX ix_contract_filter_rules_contract_scope
	ON contract_filter_rules(contract_id, scope, rule_order);


-- =============================================
-- Source: 010_add_contract_payload_transformations.sql
-- =============================================
ALTER TABLE contracts
	ADD COLUMN transformation_config jsonb NULL,
	ADD CONSTRAINT ck_contracts_transformation_config_object
		CHECK (transformation_config IS NULL OR jsonb_typeof(transformation_config) = 'object');

ALTER TABLE deliveries
	ADD COLUMN transformation_config jsonb NULL,
	ADD CONSTRAINT ck_deliveries_transformation_config_object
		CHECK (transformation_config IS NULL OR jsonb_typeof(transformation_config) = 'object');


-- =============================================
-- Source: 011_add_delivery_rate_limits.sql
-- =============================================
ALTER TABLE contracts
	ADD COLUMN IF NOT EXISTS rate_limit_per_second integer NULL,
	ADD COLUMN IF NOT EXISTS rate_limit_per_minute integer NULL,
	ADD COLUMN IF NOT EXISTS rate_limit_burst integer NULL;

ALTER TABLE contracts
	ADD CONSTRAINT ck_contracts_rate_limit_per_second_positive
		CHECK (rate_limit_per_second IS NULL OR rate_limit_per_second > 0),
	ADD CONSTRAINT ck_contracts_rate_limit_per_minute_positive
		CHECK (rate_limit_per_minute IS NULL OR rate_limit_per_minute > 0),
	ADD CONSTRAINT ck_contracts_rate_limit_burst_positive
		CHECK (rate_limit_burst IS NULL OR rate_limit_burst > 0),
	ADD CONSTRAINT ck_contracts_rate_limit_requires_rate
		CHECK (
			rate_limit_burst IS NULL
			OR rate_limit_per_second IS NOT NULL
			OR rate_limit_per_minute IS NOT NULL
		);

ALTER TABLE deliveries
	ADD COLUMN IF NOT EXISTS rate_limit_per_second integer NULL,
	ADD COLUMN IF NOT EXISTS rate_limit_per_minute integer NULL,
	ADD COLUMN IF NOT EXISTS rate_limit_burst integer NULL;

ALTER TABLE deliveries
	ADD CONSTRAINT ck_deliveries_rate_limit_per_second_positive
		CHECK (rate_limit_per_second IS NULL OR rate_limit_per_second > 0),
	ADD CONSTRAINT ck_deliveries_rate_limit_per_minute_positive
		CHECK (rate_limit_per_minute IS NULL OR rate_limit_per_minute > 0),
	ADD CONSTRAINT ck_deliveries_rate_limit_burst_positive
		CHECK (rate_limit_burst IS NULL OR rate_limit_burst > 0),
	ADD CONSTRAINT ck_deliveries_rate_limit_requires_rate
		CHECK (
			rate_limit_burst IS NULL
			OR rate_limit_per_second IS NOT NULL
			OR rate_limit_per_minute IS NOT NULL
		);

CREATE TABLE IF NOT EXISTS contract_delivery_rate_limit_state
(
	contract_id          uuid PRIMARY KEY REFERENCES contracts(id) ON DELETE CASCADE,
	tokens               double precision NOT NULL,
	last_refill_at       timestamptz      NOT NULL,
	minute_window_start  timestamptz      NOT NULL,
	minute_count         integer          NOT NULL,
	updated_at           timestamptz      NOT NULL,
	CONSTRAINT ck_contract_delivery_rate_limit_state_tokens_non_negative CHECK (tokens >= 0),
	CONSTRAINT ck_contract_delivery_rate_limit_state_minute_count_non_negative CHECK (minute_count >= 0)
);

CREATE INDEX IF NOT EXISTS ix_contract_delivery_rate_limit_state_updated_at
	ON contract_delivery_rate_limit_state(updated_at);


-- =============================================
-- Source: 012_add_delivery_ordering.sql
-- =============================================
ALTER TABLE contracts
	ADD COLUMN IF NOT EXISTS ordering_enabled boolean NOT NULL DEFAULT FALSE,
	ADD COLUMN IF NOT EXISTS ordering_key_selector text NULL;

ALTER TABLE deliveries
	ADD COLUMN IF NOT EXISTS ordering_enabled boolean NOT NULL DEFAULT FALSE,
	ADD COLUMN IF NOT EXISTS ordering_key text NULL;

CREATE INDEX IF NOT EXISTS ix_deliveries_ordering_key_created_at
	ON deliveries(ordering_key, created_at, id)
	WHERE ordering_enabled = TRUE;

CREATE TABLE IF NOT EXISTS contract_delivery_ordering_state
(
	ordering_key       text        PRIMARY KEY,
	active_delivery_id uuid        NULL REFERENCES deliveries(id) ON DELETE SET NULL,
	lease_expires_at   timestamptz NULL,
	updated_at         timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_contract_delivery_ordering_state_lease_expires_at
	ON contract_delivery_ordering_state(lease_expires_at);


-- =============================================
-- Source: 013_add_retention_cleanup_indexes.sql
-- =============================================
CREATE INDEX IF NOT EXISTS ix_outbox_messages_processed_at
	ON outbox_messages(processed_at)
	WHERE processed_at IS NOT NULL;

-- NOTE: `delivery_attempts` is not created anywhere in this consolidated baseline.
-- The original migration set appears to be missing 006_create_delivery_attempts.sql.
-- Keep this index disabled until that table definition is restored.
-- CREATE INDEX IF NOT EXISTS ix_delivery_attempts_completed_at
-- 	ON delivery_attempts(completed_at);

CREATE INDEX IF NOT EXISTS ix_deliveries_terminal_updated_at
	ON deliveries(updated_at, id)
	WHERE status IN ('Delivered', 'Dead')
	   OR (status = 'Failed' AND next_attempt_at IS NULL);


-- =============================================
-- Source: 014_add_outbox_retry_metadata.sql
-- =============================================
ALTER TABLE outbox_messages
	ADD COLUMN next_attempt_at timestamptz NULL;

ALTER TABLE outbox_messages
	ADD COLUMN last_failed_at timestamptz NULL;

ALTER TABLE outbox_messages
	ADD COLUMN last_error text NULL;


-- =============================================
-- Source: 015_add_outbox_claim_indexes.sql
-- =============================================
CREATE INDEX ix_outbox_messages_claim_ready
	ON outbox_messages(created_at, id)
	WHERE processed_at IS NULL;

CREATE INDEX ix_outbox_messages_claim_expiration
	ON outbox_messages(claim_expires_at)
	WHERE processed_at IS NULL;

CREATE INDEX ix_outbox_messages_next_attempt
	ON outbox_messages(next_attempt_at)
	WHERE processed_at IS NULL;


-- =============================================
-- Source: 016_add_delivery_contract_snapshots.sql
-- =============================================
ALTER TABLE deliveries
	ADD COLUMN IF NOT EXISTS auth_ref text NULL,
	ADD COLUMN IF NOT EXISTS auth_version integer NULL,
	ADD COLUMN IF NOT EXISTS transformation_version integer NULL,
	ADD COLUMN IF NOT EXISTS schema_version integer NULL;

ALTER TABLE deliveries
	ADD CONSTRAINT ck_deliveries_auth_version_positive
		CHECK (auth_version IS NULL OR auth_version > 0),
	ADD CONSTRAINT ck_deliveries_transformation_version_positive
		CHECK (transformation_version IS NULL OR transformation_version > 0),
	ADD CONSTRAINT ck_deliveries_schema_version_positive
		CHECK (schema_version IS NULL OR schema_version > 0);


-- =============================================
-- Source: 017_add_contract_snapshot_columns.sql
-- =============================================
ALTER TABLE contracts
	ADD COLUMN IF NOT EXISTS auth_ref text NULL,
	ADD COLUMN IF NOT EXISTS auth_version integer NULL,
	ADD COLUMN IF NOT EXISTS transformation_version integer NULL;

UPDATE contracts
SET auth_ref = CASE
		WHEN lower(COALESCE(destination_config #>> '{Authentication,Credential,Kind}', '')) = 'ref'
			THEN destination_config #>> '{Authentication,Credential,Value}'
		ELSE auth_ref
	END,
	auth_version = CASE
		WHEN (destination_config #>> '{Authentication,Credential,Version}') ~ '^[0-9]+$'
			THEN (destination_config #>> '{Authentication,Credential,Version}')::integer
		ELSE auth_version
	END,
	transformation_version = CASE
		WHEN (transformation_config #>> '{Version}') ~ '^[0-9]+$'
			THEN (transformation_config #>> '{Version}')::integer
		ELSE transformation_version
	END
WHERE auth_ref IS NULL
	OR auth_version IS NULL
	OR transformation_version IS NULL;

ALTER TABLE contracts
	ADD CONSTRAINT ck_contracts_auth_version_positive
		CHECK (auth_version IS NULL OR auth_version > 0),
	ADD CONSTRAINT ck_contracts_transformation_version_positive
		CHECK (transformation_version IS NULL OR transformation_version > 0);


-- =============================================
-- Source: 018_add_delivery_scheduler_indexes.sql
-- =============================================
CREATE INDEX IF NOT EXISTS ix_deliveries_scheduler_due
	ON deliveries(status, next_attempt_at, created_at, id)
	WHERE next_attempt_at IS NOT NULL
	  AND status IN ('Pending', 'Failed');


-- =============================================
-- Source: 019_add_delivery_operation_audits.sql
-- =============================================
CREATE TABLE delivery_operation_audits
(
	id                uuid        PRIMARY KEY,
	delivery_id       uuid        NOT NULL REFERENCES deliveries(id) ON DELETE CASCADE,
	operation_id      uuid        NOT NULL,
	operation         text        NOT NULL,
	operator_id       text        NOT NULL,
	request_snapshot  jsonb       NOT NULL,
	response_snapshot jsonb       NOT NULL,
	created_at        timestamptz NOT NULL,
	CONSTRAINT uq_delivery_operation_audits_delivery_operation UNIQUE (delivery_id, operation_id)
);

CREATE INDEX ix_delivery_operation_audits_delivery_created
	ON delivery_operation_audits(delivery_id, created_at DESC);


-- =============================================
-- Source: 019_backfill_contract_auth_snapshot_columns.sql
-- =============================================
UPDATE contracts
SET auth_ref = CASE
		WHEN lower(COALESCE(destination_config #>> '{Authentication,Credential,Kind}', '')) = 'ref'
			THEN destination_config #>> '{Authentication,Credential,Value}'
		ELSE auth_ref
	END,
	auth_version = CASE
		WHEN lower(COALESCE(destination_config #>> '{Authentication,Credential,Kind}', '')) = 'ref'
			 AND (destination_config #>> '{Authentication,Credential,Version}') ~ '^[0-9]+$'
			 AND (destination_config #>> '{Authentication,Credential,Version}')::integer > 0
			THEN (destination_config #>> '{Authentication,Credential,Version}')::integer
		WHEN lower(COALESCE(destination_config #>> '{Authentication,Credential,Kind}', '')) = 'ref'
			THEN COALESCE(auth_version, 1)
		ELSE auth_version
	END
WHERE auth_ref IS NULL
   OR auth_version IS NULL;


-- =============================================
-- Source: 020_create_delivery_replay_jobs.sql
-- =============================================
CREATE TABLE delivery_replay_jobs
(
	id               uuid        PRIMARY KEY,
	operation_id     uuid        NOT NULL,
	operator_id      text        NOT NULL,
	request_snapshot jsonb       NOT NULL,
	status           text        NOT NULL,
	event_type       text        NULL,
	customer_id      text        NULL,
	contract_id      uuid        NULL REFERENCES contracts(id) ON DELETE SET NULL,
	created_from     timestamptz NULL,
	created_to       timestamptz NULL,
	batch_limit      int         NOT NULL,
	replayed_count   int         NOT NULL DEFAULT 0,
	error            text        NULL,
	created_at       timestamptz NOT NULL,
	updated_at       timestamptz NOT NULL,
	completed_at     timestamptz NULL,
	CONSTRAINT uq_delivery_replay_jobs_operation UNIQUE (operation_id),
	CONSTRAINT ck_delivery_replay_jobs_batch_limit_positive CHECK (batch_limit > 0),
	CONSTRAINT ck_delivery_replay_jobs_replayed_count_non_negative CHECK (replayed_count >= 0)
);

CREATE INDEX ix_delivery_replay_jobs_status_created
	ON delivery_replay_jobs(status, created_at);


-- =============================================
-- Source: 021_create_audit_logs.sql
-- =============================================
CREATE TABLE audit_logs
(
	id           uuid        PRIMARY KEY,
	category     text        NOT NULL,
	action       text        NOT NULL,
	actor_id     text        NULL,
	customer_id  text        NULL,
	contract_id  uuid        NULL,
	delivery_id  uuid        NULL,
	event_id     uuid        NULL,
	operation_id uuid        NULL,
	details      jsonb       NOT NULL,
	created_at   timestamptz NOT NULL,
	expires_at   timestamptz NOT NULL
);

CREATE INDEX ix_audit_logs_expires_at
	ON audit_logs(expires_at);

CREATE INDEX ix_audit_logs_created_at
	ON audit_logs(created_at DESC);

CREATE INDEX ix_audit_logs_customer_created
	ON audit_logs(customer_id, created_at DESC)
	WHERE customer_id IS NOT NULL;

CREATE INDEX ix_audit_logs_contract_created
	ON audit_logs(contract_id, created_at DESC)
	WHERE contract_id IS NOT NULL;

CREATE INDEX ix_audit_logs_delivery_created
	ON audit_logs(delivery_id, created_at DESC)
	WHERE delivery_id IS NOT NULL;

CREATE INDEX ix_audit_logs_event_created
	ON audit_logs(event_id, created_at DESC)
	WHERE event_id IS NOT NULL;

CREATE INDEX ix_audit_logs_operation
	ON audit_logs(operation_id)
	WHERE operation_id IS NOT NULL;


-- =============================================
-- Source: 022_add_contract_name.sql
-- =============================================
ALTER TABLE contracts
	ADD COLUMN IF NOT EXISTS name text NULL;


-- =============================================
-- Source: 023_create_destination_health.sql
-- =============================================
CREATE TABLE destination_health
(
	contract_id            uuid             PRIMARY KEY REFERENCES contracts(id) ON DELETE CASCADE,
	customer_id            text             NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
	destination_type       text             NOT NULL,
	consecutive_failures   int              NOT NULL DEFAULT 0,
	last_success_at        timestamptz      NULL,
	last_failure_at        timestamptz      NULL,
	last_attempt_at        timestamptz      NOT NULL,
	last_latency_ms        double precision NULL,
	avg_latency_ms         double precision NULL,
	latency_sample_count   bigint           NOT NULL DEFAULT 0,
	health_status          text             NOT NULL,
	created_at             timestamptz      NOT NULL,
	updated_at             timestamptz      NOT NULL,
	CONSTRAINT ck_destination_health_consecutive_failures_non_negative CHECK (consecutive_failures >= 0),
	CONSTRAINT ck_destination_health_latency_sample_count_non_negative CHECK (latency_sample_count >= 0)
);

CREATE INDEX ix_destination_health_customer_id
	ON destination_health(customer_id);

CREATE INDEX ix_destination_health_customer_status
	ON destination_health(customer_id, health_status);


-- =============================================
-- Source: 024_create_admin_user_roles.sql
-- =============================================
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


-- =============================================
-- Source: 025_create_settings.sql
-- =============================================
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


-- =============================================
-- Source: 026_create_admin_local_credentials.sql
-- =============================================
CREATE TABLE admin_local_credentials
(
	subject       text        PRIMARY KEY,
	password_hash text        NOT NULL,
	created_at    timestamptz NOT NULL,
	updated_at    timestamptz NOT NULL,
	CONSTRAINT fk_admin_local_credentials_users
		FOREIGN KEY (subject) REFERENCES admin_users(subject)
		ON DELETE CASCADE
);

CREATE INDEX ix_admin_local_credentials_updated_at
	ON admin_local_credentials(updated_at DESC);


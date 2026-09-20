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

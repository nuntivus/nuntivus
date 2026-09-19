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

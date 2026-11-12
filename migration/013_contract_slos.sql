CREATE TABLE contract_slo_policies (
	contract_id uuid PRIMARY KEY REFERENCES contracts(id) ON DELETE CASCADE,
	success_target numeric(8,5) NOT NULL CHECK (success_target > 0 AND success_target <= 100),
	latency_target numeric(8,5) NOT NULL CHECK (latency_target > 0 AND latency_target <= 100),
	latency_threshold_seconds integer NOT NULL CHECK (latency_threshold_seconds BETWEEN 1 AND 604800),
	success_deadline_seconds integer NOT NULL DEFAULT 86400 CHECK (success_deadline_seconds BETWEEN 1 AND 604800),
	monitoring_started_at timestamptz NOT NULL,
	updated_at timestamptz NOT NULL,
	CHECK (latency_threshold_seconds <= success_deadline_seconds)
);

CREATE TABLE delivery_slo_observations (
	delivery_id uuid PRIMARY KEY,
	contract_id uuid NOT NULL,
	customer_id text NOT NULL,
	destination_binding_id uuid NULL,
	created_at timestamptz NOT NULL,
	first_delivered_at timestamptz NULL,
	measurement_closed boolean NOT NULL DEFAULT false,
	excluded boolean NOT NULL,
	exclusion_reason text NULL,
	CHECK (NOT excluded OR exclusion_reason IS NOT NULL)
);
CREATE INDEX ix_delivery_slo_observations_contract_created ON delivery_slo_observations (contract_id, created_at);

CREATE FUNCTION record_delivery_slo_observation() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE event_metadata jsonb;
BEGIN
	IF TG_OP = 'INSERT' THEN
		SELECT metadata INTO event_metadata FROM events WHERE id = NEW.event_id;
		INSERT INTO delivery_slo_observations (delivery_id, contract_id, customer_id, destination_binding_id,
			created_at, first_delivered_at, excluded, exclusion_reason)
		VALUES (NEW.id, NEW.contract_id, NEW.customer_id, NEW.destination_binding_id, NEW.created_at,
			CASE WHEN NEW.status = 'Delivered' THEN NEW.updated_at ELSE NULL END,
			COALESCE(event_metadata ->> 'nuntivus.test' = 'true', false) OR COALESCE(event_metadata ? 'nuntivus.replay.sourceEventId', false),
			CASE WHEN event_metadata ->> 'nuntivus.test' = 'true' THEN 'Test'
				 WHEN event_metadata ? 'nuntivus.replay.sourceEventId' THEN 'EventReplay' ELSE NULL END)
		ON CONFLICT DO NOTHING;
	ELSIF NEW.status = 'Delivered' AND OLD.status <> 'Delivered' THEN
		UPDATE delivery_slo_observations SET first_delivered_at = COALESCE(first_delivered_at, NEW.updated_at)
		WHERE delivery_id = NEW.id AND NOT measurement_closed;
	END IF;
	RETURN NEW;
END;
$$;
CREATE TRIGGER tr_delivery_slo_observation AFTER INSERT OR UPDATE OF status ON deliveries
	FOR EACH ROW EXECUTE FUNCTION record_delivery_slo_observation();

-- Historical delivery rows are not backfilled: monitoring starts when a policy is configured.
-- Observations intentionally have no delivery/event FK so retention cannot erase the denominator.

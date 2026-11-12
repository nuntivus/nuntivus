ALTER TABLE events
	ADD COLUMN input_schema_version integer NULL,
	ADD COLUMN schema_validation_status text NOT NULL DEFAULT 'Unknown',
	ADD CONSTRAINT ck_events_schema_validation_status
		CHECK (schema_validation_status IN ('Unknown', 'NotConfigured', 'Valid')),
	ADD CONSTRAINT ck_events_input_schema_validation
		CHECK (
			(schema_validation_status = 'Valid' AND input_schema_version IS NOT NULL AND input_schema_version > 0)
			OR (schema_validation_status IN ('Unknown', 'NotConfigured') AND input_schema_version IS NULL)
		);

CREATE UNIQUE INDEX ux_event_schema_versions_active
	ON event_schema_versions (event_type)
	WHERE state_code = 'Active';

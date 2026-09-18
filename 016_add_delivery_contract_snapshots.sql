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
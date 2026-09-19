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
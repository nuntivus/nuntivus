ALTER TABLE contracts
	ADD COLUMN transformation_config jsonb NULL,
	ADD CONSTRAINT ck_contracts_transformation_config_object
		CHECK (transformation_config IS NULL OR jsonb_typeof(transformation_config) = 'object');

ALTER TABLE deliveries
	ADD COLUMN transformation_config jsonb NULL,
	ADD CONSTRAINT ck_deliveries_transformation_config_object
		CHECK (transformation_config IS NULL OR jsonb_typeof(transformation_config) = 'object');

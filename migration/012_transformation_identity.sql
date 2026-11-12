ALTER TABLE contracts ADD COLUMN transformation_identity text NULL;
ALTER TABLE deliveries ADD COLUMN transformation_identity text NULL;

ALTER TABLE contracts ADD CONSTRAINT ck_contracts_transformation_identity
	CHECK (transformation_identity IS NULL OR (transformation_config IS NOT NULL AND transformation_identity ~ '^[0-9a-f]{64}$'));
ALTER TABLE deliveries ADD CONSTRAINT ck_deliveries_transformation_identity
	CHECK (transformation_identity IS NULL OR (transformation_config IS NOT NULL AND transformation_identity ~ '^[0-9a-f]{64}$'));

CREATE INDEX ix_deliveries_transformation_identity ON deliveries (transformation_identity)
	WHERE transformation_identity IS NOT NULL;

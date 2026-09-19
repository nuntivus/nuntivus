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
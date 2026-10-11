ALTER TABLE deliveries
	ADD COLUMN attempt_number_offset int NOT NULL DEFAULT 0;

UPDATE deliveries d
SET attempt_number_offset = GREATEST(a.max_attempt_number - d.attempt_count, 0)
FROM (
	SELECT delivery_id, MAX(attempt_number) AS max_attempt_number
	FROM delivery_attempts
	GROUP BY delivery_id
) a
WHERE d.id = a.delivery_id;

CREATE INDEX ix_outbox_messages_claim_ready
	ON outbox_messages(created_at, id)
	WHERE processed_at IS NULL;

CREATE INDEX ix_outbox_messages_claim_expiration
	ON outbox_messages(claim_expires_at)
	WHERE processed_at IS NULL;

CREATE INDEX ix_outbox_messages_next_attempt
	ON outbox_messages(next_attempt_at)
	WHERE processed_at IS NULL;

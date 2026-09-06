CREATE TABLE outbox_messages
(
    id            uuid        PRIMARY KEY,
    type          text        NOT NULL,
    payload       jsonb       NOT NULL,
    created_at    timestamptz NOT NULL,
    processed_at  timestamptz NULL,
    attempt_count integer     NOT NULL DEFAULT 0
);

CREATE INDEX ix_outbox_messages_pending
    ON outbox_messages(created_at)
    WHERE processed_at IS NULL;
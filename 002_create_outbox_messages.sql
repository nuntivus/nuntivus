CREATE TABLE outbox_messages
(
    id            uuid        PRIMARY KEY,
    event_id      uuid        NOT NULL,
    type          text        NOT NULL,
    payload       jsonb       NOT NULL,
    created_at    timestamptz NOT NULL,
    processed_at  timestamptz NULL,
    attempt_count integer     NOT NULL DEFAULT 0,
    CONSTRAINT fk_outbox_messages_event
        FOREIGN KEY (event_id)
        REFERENCES events (id)
        ON DELETE CASCADE
);

CREATE INDEX ix_outbox_messages_event_id
    ON outbox_messages(event_id);

CREATE INDEX ix_outbox_messages_pending
    ON outbox_messages(created_at)
    WHERE processed_at IS NULL;
CREATE TABLE events
(
    id          uuid        PRIMARY KEY,
    type        text        NOT NULL,
    idempotency_key  text   NOT NULL,
    source_id   uuid        NOT NULL,
    occurred_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL,
    metadata    jsonb       NOT NULL DEFAULT '{}'::jsonb,
    payload     jsonb       NOT NULL,
    CONSTRAINT uq_events_source_idempotency UNIQUE (source_id, idempotency_key)
);

CREATE INDEX ix_events_type
    ON events(type);

CREATE INDEX ix_events_idempotency_key
    ON events(idempotency_key);

CREATE INDEX ix_events_source_id
    ON events(source_id);

CREATE INDEX ix_events_received_at
    ON events(received_at);
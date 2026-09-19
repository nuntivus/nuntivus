CREATE TABLE event_replays
(
	source_event_id   uuid        NOT NULL REFERENCES events(id) ON DELETE CASCADE,
	replay_key        text        NOT NULL,
	replayed_event_id uuid        NOT NULL REFERENCES events(id) ON DELETE CASCADE,
	created_at        timestamptz NOT NULL,
	CONSTRAINT pk_event_replays PRIMARY KEY (source_event_id, replay_key),
	CONSTRAINT uq_event_replays_replayed_event UNIQUE (replayed_event_id)
);

CREATE INDEX ix_event_replays_replayed_event_id
	ON event_replays(replayed_event_id);

CREATE INDEX ix_event_replays_created_at
	ON event_replays(created_at);
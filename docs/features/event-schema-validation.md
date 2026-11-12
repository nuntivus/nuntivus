# Event schema validation

Nuntivus validates new source events against the active schema for their event type. This applies to HTTP ingestion and managed pull-source ingestion through the shared application service.

## Schema registration

Register JSON Schema 2020-12 documents through the existing event-definition schema API. A document without `$schema` is interpreted as 2020-12. Other declared dialects are rejected. Object and boolean schemas are supported; `{}` and `true` accept any payload, and `false` rejects every payload.

Documents are checked against the 2020-12 meta-schema before storage and activation. Validation uses JsonSchema.Net behind `IEventSchemaValidator`.

References must resolve within the root document, using JSON pointers or local anchors. Remote references, nested `$id` values, custom vocabularies, and reference cycles that do not descend into a child payload are rejected. Reference traversal is limited to depth 64. Examples and constant data are not interpreted as subschemas. `format` follows 2020-12 annotation semantics rather than enabling format assertion globally.

## Acceptance policy

- With an active schema: a new payload must conform before an event or outbox entry is committed.
- Without an active schema: ingestion remains allowed and records `NotConfigured`.
- An existing `(sourceId, idempotencyKey)` returns the original event, even after a different schema is activated. It is not revalidated or rewritten.
- Validation errors include a JSON pointer in `errors[].path` and a failing keyword in `errors[].keyword`, without echoing rejected payload values. HTTP ingestion returns the normal validation response (400).
- A broker or scan ingestion failure follows its existing explicit failure/retry behavior; it must not acknowledge, delete, commit an offset, or advance a cursor as though the event were accepted.

## Recorded event fields

Event list/detail responses include:

- `inputSchemaVersion`: the selected input schema version, or null.
- `schemaValidationStatus`: `Valid`, `NotConfigured`, or `Unknown`.

These are first-class PostgreSQL columns. `Unknown` means validation was not recorded, including historical events created before this feature. It must not be interpreted as either a validation failure or a successful validation against today's schema.

Input validation is separate from the output schema targeted by each customer contract. Replay preserves the original input-validation metadata; it does not claim that a historical event passed a newly activated input schema. The event detail page displays the recorded input version and validation status.

## Migration

Apply `011_event_schema_validation.sql` using the existing migration runner. It initializes historical rows to `Unknown` and enforces a single active schema version per event type. Schema activation serializes updates for the same event definition.

## Verification

The unit suite covers document validity, 2020-12 keywords, local references, safe recursion, field paths, validation metadata invariants, no-schema ingestion and duplicate handling. Integration tests cover schema registration, accepted/rejected ingestion, persisted metadata, replay and immediate activation/deprecation behavior. Browser tests cover the event-detail status labels.

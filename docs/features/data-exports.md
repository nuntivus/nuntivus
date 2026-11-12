# CSV and JSON exports

Operators and administrators can download metadata-only Events, Deliveries and Contracts from their dashboard list pages. Viewers and customer portal users cannot export. Downloads use the authenticated user principal, preserve applied filters, and never put bearer tokens in URLs.

The Failed deliveries page also provides these controls, using its applied customer, contract, failure-category and Failed/Dead status filters. Unsubmitted form edits do not affect downloads. Invalid contract IDs disable export; table pagination and sorting are excluded.

## API

`GET /v1/exports/{entity}?format=csv|json` where entity is `events`, `deliveries` or `contracts`.

Supported filters:

- Events: `id`, `type`, `source` (source UUID string), `occurredFrom`, `occurredTo`, `receivedFrom`, `receivedTo`.
- Deliveries: `id`, `eventId`, `contractId`, `customerId`, `eventType`, `status`, `destinationType`, `failureCategory`, `createdFrom`, `createdTo`. Failure category matches any unsuccessful attempt, like the administrative delivery list.
- Contracts: `id`, `customerId`, `enabled`, `eventType`, `destinationType`, `search`, `createdFrom`, `createdTo`. Destination type matches linked bindings; search uses the same ID/customer/name/event-type matching as the administrative list.

Filters use inclusive time bounds. Strings are bounded at 500 characters. Export order is fixed: oldest received event first, or oldest created delivery/contract first, then UUID. Table sort/page settings are deliberately ignored. The event page's ID filter becomes an exact server-side UUID filter for exports. Invalid UUID/time/bool model values produce HTTP 400.

Each export opens one PostgreSQL repeatable-read, read-only snapshot. A bounded preflight checks at most 10,001 matching rows; more than 10,000 is rejected before response streaming, never silently truncated. Narrow filters to divide larger datasets. A forward-only database reader streams this snapshot without offset pagination or resumable cursors. Concurrent inserts, updates and retention do not shift results within that download. This is not a reusable snapshot across requests.

The snapshot also excludes records whose creation/receipt timestamp is after the server cutoff. Response headers `X-Export-Count` and `X-Export-Cutoff` describe the result. JSON has a `cutoff`, `count`, `notice` and `items` envelope. CSV includes a header even for zero rows; each field is quoted, quotes doubled, timestamps/numbers use invariant formatting. Untrusted text with controls or a leading (after whitespace) `=`, `+`, `-`, or `@` is apostrophe-prefixed to reduce spreadsheet formula injection. JSON retains ordinary text values.

## Data minimization

Arbitrary customer JSON cannot be safely redacted without field-level classification rules. These exports therefore omit payloads and metadata entirely, rather than make an unreliable redaction promise. Destination configuration, endpoints, authentication references, credentials, idempotency keys, contract names, raw error messages and delivery response bodies are also excluded.

- Events: ID, type, source ID, occurred/received timestamps, recorded input schema version and validation status.
- Deliveries: ID, event/contract/customer IDs, event/destination types, status, attempt count, created/updated timestamps.
- Contracts: ID, customer ID, enabled state, created/updated timestamps.

Identifiers and type names can themselves contain business information. Only authorized staff should distribute downloaded files; metadata-only does not mean anonymous.

## Cancellation, auditing and limits

The shared download controls show Cancel while fetching and cancel on component unmount. The server propagates request cancellation to database reads and response flushes and disposes the snapshot/connection. Browser download creation occurs after receiving the complete file, so a failed/truncated stream is not offered as a successful download.

A transactional `export/started` audit must succeed before any file bytes are sent. It records actor, export operation ID, filters, selected columns, cutoff and count, not exported records. Completion/cancellation/failure records correlate by operation ID and report rows processed. Final outcome audit is attempted with an independent five-second timeout; failures are logged because a file already sent cannot be rolled back. A crash or audit-store failure can leave only the start record. Row counts reflect rows written to the server response, not proof that a recipient saved them.

Responses are no-store attachments. Queries have 30-second preflight and 60-second reader command timeouts; slow consumers can hold snapshots until cancellation/disconnection. There are no background jobs, signed download URLs, stored files, automatic broad payload redaction, customer exports, or resumable download tokens in this version. No migration is required.

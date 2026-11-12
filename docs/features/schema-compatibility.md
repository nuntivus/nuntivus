# Schema compatibility and activation

Before activating a schema, Nuntivus compares it with the currently active version for that event type. If none exists, the first valid schema can be activated without a compatibility baseline.

## Directions

- **Backward:** payloads accepted by the current schema must remain acceptable to the proposed schema.
- **Forward:** payloads accepted by the proposed schema must remain acceptable to the current schema.

Use the proposed version's configured compatibility mode. Comparison does not transform payloads, query sample events, or mutate live configuration.

`GET /v1/event-definitions/{eventType}/schemas/{version}/compatibility` returns the proposed version, baseline version, mode, status, and field-path findings. An optional `againstVersion` query parameter selects a specific existing version for analysis only. Activation always checks the live active baseline, never a caller-selected historical version.

## Results and supported rules

- `Compatible`: the supported comparison rules find no acceptance narrowing in the selected direction.
- `Breaking`: a conservative rule detects a potentially incompatible constraint change, such as a required field, narrower type/enum, tighter bound, restricted property, or array constraint.
- `Indeterminate`: the analyzer cannot prove safety for the changed constructs or numeric precision involved. This is not treated as compatible.

The initial rule set covers boolean schemas; type sets and integer-to-number widening; enum/const constraints; exact decimal numeric bounds and multiples; string sizes; nested object properties, required fields and additional properties; array item schemas, sizes and uniqueness.

Changed regular expressions and complex constructs such as composition, references, conditional schemas, tuple schemas and unevaluated-property rules require review. Unchanged complete schema documents are compatible, including complex schemas. Annotation-only fields do not affect acceptance. `format` uses the validator's 2020-12 annotation semantics.

This is deliberately conservative, not a general JSON Schema theorem prover. Independent constraints can imply tighter restrictions than the analyzer can establish, so some potentially breaking reports require human review. Out-of-range or rounded numeric values produce `Indeterminate`, not a false-safe result. Reports contain at most 100 displayed findings; the overall status accounts for all findings.

## Activation policy

`POST /v1/event-definitions/{eventType}/schemas/{version}/activate` still permits an empty request body for normal activation. Breaking or indeterminate changes return 409 and leave the active schema unchanged.

An authenticated administrator can explicitly submit `overrideCompatibility: true` and a non-empty `reason` of at most 2000 characters. Operators cannot override checks, and identity/administrator authority are taken from the authenticated principal, not the request body.

The activation and its audit record commit in one transaction. Audit details include the full request, comparison report and activated schema response. The reason and actor identify the administrative decision. If audit persistence fails, activation rolls back.

Activation locks the event definition before reading the baseline, so concurrent changes are compared against the actual serialized current version. Deprecation takes the same lock, and the database enforces one active version per event type. A report displayed earlier is advisory; activation re-evaluates it under the lock.

## Dashboard

Select **View schema** on an event-definition detail page to see the compatibility report. Administrators can enter an override reason and use the shared confirmation dialog. Operators see the report but not override controls. Unsaved override reasons use the shared route-change-only navigation guard.

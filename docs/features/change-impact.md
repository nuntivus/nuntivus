# Read-only change impact analysis

Impact reports inventory affected contracts, customers and destination bindings without activating schemas, saving contracts, creating events/deliveries/outbox records, or executing a destination. They contain identifiers and destination types, not destination configuration or credentials.

## Schema changes

`GET /v1/event-definitions/{eventType}/schemas/{version}/impact?page=1&pageSize=20&sampleSize=10`

The report includes subscribed contracts, including disabled ones, their customer IDs, pinned output versions, transformation identities, binding IDs and enabled flags. Contracts are paged (1–100 per request). Up to 20 recent events of that type are fetched and restricted to each contract's linked sources; a contract may have fewer or no applicable samples.

The analyzer checks backward input acceptance against the currently active input schema, regardless of the candidate's configured activation direction. This asks whether existing producer payloads remain ingestible. It does not change pinned output-schema targets or queued deliveries. Changed transformation behavior and downstream consumer expectations require review. Static indeterminate findings are never shown as proven safe.

## Proposed transformations

`POST /v1/contracts/{contractId}/transformation-impact`

Submit `eventType`, `transformation` (the same versioned configuration used by contract editing, or null to remove it), and `sampleSize` (0–20). The proposed configuration is validated using the contract-save rules. Samples run through the same bounded payload preparer and recorded output-schema version as actual delivery.

Unchanged transformation content is compatible; any changed transformation remains `ReviewRequired` even if samples pass. A sampled failure produces `Breaking`. Samples do not prove compatibility. Output payloads are not included in the impact response.

The contract edit page analyzes its unsaved transformation while other routing and schema settings use the saved contract. Editing the configuration invalidates the displayed report. Unsaved raw-JSON edits and invalid configuration disable analysis.

## Interpretation

Statuses are `Compatible`, `Breaking`, or `ReviewRequired`. `Breaking` indicates conservative static input narrowing or a sampled rejection, not that every consumer will fail. Reports are read-only point-in-time observations, not an atomic configuration snapshot or an activation authorization token. Review findings and refresh before applying changes; schema activation independently rechecks its live baseline under lock.

Source selection is applied to sample evidence. Filters, enabled customers/contracts and enabled destination bindings still determine actual routing; the inventory intentionally includes potentially affected disabled configuration. Recent sample selection is global for the event type, not a guarantee of representative coverage for every source or customer.

Both endpoints require the existing authenticated administrative Viewer policy. They do not expose a customer-portal endpoint; future customer-facing exposure must apply internal membership-based tenant scope.

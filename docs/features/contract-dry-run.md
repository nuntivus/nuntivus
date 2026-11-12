# Full contract dry runs

Dry runs are read-only simulations, separate from explicit test-send. They never invoke destination executors or connection testers and have no unit-of-work, write, outbox, audit, ordering lease or rate-limit reservation dependency.

## API

- `POST /v1/contracts/{contractId}/dry-run`: simulate a saved contract, optionally replacing its configuration with a complete draft for the same customer.
- `POST /v1/contracts/dry-run`: simulate a new complete draft without creating it.

Request fields are `eventType`, `sourceId`, object `payload`, optional string-valued `metadata`, optional `draft` (the create-contract API shape), and `draftEnabled` (defaults true). A draft is a complete replacement, not a patch. Inline draft destination configuration uses the normal `configuration` discriminator shape; linked destinations use `destinationIds`.

The report includes customer/source/routing checks, active input-schema selection and validation, filters, transformation configuration, prepared output and selected output-schema version, transformation identity, and destination checks. `wouldDeliver` means the modeled checks passed, not that network delivery is guaranteed.

Failures are reported as stages where possible. Malformed input and structurally invalid schema targets return validation errors. The payload is capped at 1,000,000 characters. Version-2 transformations retain their own complexity/execution limits.

## What is and is not simulated

The shared filter evaluator and payload preparer are the same ones used by matching/dispatch. Input validation uses the active input schema; output validation uses the contract's pinned version. The report does not confuse these two schema roles.

Saved destination configurations and referenced resources are reported without exposing secrets. Inline draft configurations use the existing local configuration handlers. No credential lookup that performs a connection attempt, DNS reachability test, broker publish or HTTP request is performed. Existing validated saved destination configurations are identified as `NotConnected`, not as verified live connections.

Rate limits, ordering leases, retries, source authentication and destination reachability are explicitly `NotSimulated`. The report does not reserve capacity or claim to reproduce concurrent runtime behavior. It is not a substitute for complete contract-save validation or connection testing.

## Dashboard

The Test events page includes **Full dry run** for a saved contract, while its existing **Send test event** remains explicit and confirmed. The contract edit page can simulate unsaved options and linked destinations; detached saved destination snapshots are intentionally tested from the saved-contract Test events page rather than silently omitted from an edited draft. Results are hidden when the relevant input/configuration changes.

Both endpoints require the existing authenticated administrative Viewer policy. Customer-facing exposure requires membership-based scope and is not provided by these admin routes.

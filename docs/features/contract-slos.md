# Contract SLOs and error budgets

`GET /v1/contracts/{id}/slo` reports an existing policy. `PUT` configures `successTarget`, `latencyTarget` (percentages greater than zero up to 100, at most five decimal places), `latencyThresholdSeconds`, and `successDeadlineSeconds` (default 86400). Deadlines are 1–604800 seconds and latency cannot exceed the success deadline. Reads require administrative Viewer; configuration requires Operator and uses principal-derived identity with transactional audit.

## Measurement policy

- Rolling 30-day UTC creation cohort, measured from logical delivery creation, not the producer's occurred timestamp.
- Each destination delivery is one observation. Retries, requeues and attempt resets do not create extra observations.
- Success means first successful completion no later than its configurable deadline, default 24 hours. Latency means first successful completion within the latency threshold.
- Each metric scores only cohorts old enough to have exhausted its deadline. Younger deliveries are pending even if already successful. This prevents completed-only selection bias.
- Failed, dead, cancelled and still-retrying deliveries count as bad once their deadline expires. Cancellation never removes the original obligation.
- Test events and manually replayed events are excluded. The ingestion API reserves the `nuntivus.` metadata namespace so publishers cannot forge these exclusions. Duplicate ingestion remains idempotent.
- Delivery replay jobs reuse original delivery rows: replay closes the original observation before re-execution, so replay success cannot repair the original obligation. Normal retry within the deadline can still succeed.
- The first success timestamp is immutable. A late success does not repair an expired deadline.
- No eligible traffic produces null attainment, never a claim of 100% compliance. Allowed failures = eligible × (1 − target/100). Remaining failures can be negative. A 100% target has zero allowed failures and no meaningful remaining-budget percentage.

## Coverage and retention

Monitoring starts on initial configuration. A changed policy starts a new monitoring period, clearly shown as a partial window until 30 days are covered. Identical repeated PUT requests preserve monitoring start and do not duplicate audit. Previous policy and the new configuration response are included in audit.

Migration `013_contract_slos.sql` creates explicit SLO policy and observation columns plus a transactional delivery trigger. It does not backfill historical observations or claim historical completeness. Observations are independent of event/delivery foreign keys, so ordinary retention cannot erase failures or denominators. Only observations after the monitoring start participate. Snapshot facts include customer, contract, binding, creation, first success and exclusion reason; no payloads or credentials are retained.

A dedicated `BackgroundService` deletes observations older than 45 days in bounded batches. This horizon exceeds the rolling reporting window. Report aggregation is computed from stored facts on read, without an aggregation scheduler or delayed eventual-consistency cache. Delivery updates and observation updates commit together. Incoming events matched later start their delivery clock when the logical delivery is created; producer-to-ingestion latency is not included.

The contract detail page displays success/latency budgets, pending observations and partial coverage, and provides a policy form with route-change-only unsaved prompts and shared confirmation for policy changes.

# Product feature verification

## Implemented scope

The product roadmap implements ingestion JSON Schema validation; conservative schema compatibility and audited activation; bounded typed transformations and output validation; read-only impact analysis; network-free contract dry runs; per-contract SLOs; in-app/webhook alerts; invitation-only read-only customer access with local/Clerk providers; and staff-only CSV/JSON metadata exports.

The .NET/TypeScript SDKs and declarative configuration CLI were explicitly deferred. They are not release deliverables for this verification pass.

## Automated coverage

Final verification results: workspace build successful; 319 integration tests
passed; 253 unit tests passed; dashboard build successful; all 66 dashboard
tests passed after the test maintenance described below. The
production-only npm dependency audit reported zero vulnerabilities.

- Unit tests cover schema validation/compatibility, deterministic transformations, shared preparation, endpoint safety and spreadsheet-safe formatting.
- Integration tests exercise API permissions, persisted snapshots, atomic audits, dry-run no-write guarantees, SLO replay/retention accounting, alert lifecycle/leases, portal issuer-qualified membership isolation, credential recovery and consistent metadata exports.
- Cross-feature assertions verify that an alerting dead delivery is exportable without destination configuration and that customer portal tokens cannot use staff exports.
- `Product_migrations_apply_and_validate_with_production_runner` applies the actual migration directory to fresh PostgreSQL, reapplies without changes, validates history and checks the SLO/alert/portal tables. Older migration tests cover historical schema-validation rows and uniqueness constraints.
- Dashboard browser journeys mock API boundaries and exercise schema history, compatibility/impact, prepared-output previews, SLO configuration, alerts, customer login and permission-gated downloads. These are not live cloud/network acceptance tests.

Run the backend integration and unit projects with `dotnet test`, serially relative to workspace builds. In the dashboard, run `npm run build` and `node --test tests/*.test.mjs` against a running Vite instance (`PLAYWRIGHT_BASE_URL` overrides its address). Run `npm audit --omit=dev` separately so unrelated test failures do not hide the dependency result.

## Frontend regression test maintenance

Two outdated assertions were aligned with existing feature behavior without changing production code:

- `customer-detail.e2e.test.mjs`: the contract creation test now selects a shared destination by its accessible label, verifies required-selection validation and confirms the selected destination reaches the review step.
- `destination-type.test.mjs`: the HTTP default serialization assertion now includes the existing method, headers and optional timeout fields.

The contract-health test fixture explicitly returns 404 for an unconfigured SLO policy instead of an invalid paged response.

## Reviewed bug regressions

- Portal provider ownership prevents Clerk refresh from clearing local sessions; lifecycle tests cover sign-in remount after logout and stale refresh completion after local login.
- Migration `016_destination_failure_counters.sql` isolates consecutive failures per destination binding. Interleaved healthy/failing outcomes cannot suppress firing or cause premature recovery. Historical binding counters are not backfilled.
- Schema impact uses the candidate compatibility mode and retains Breaking status even when a contract has a transformation.
- Concurrent replay requests return one replay identity without duplicate outbox/audit writes; a deterministic unit test also covers the losing event-insert conflict path.
- Alert evaluation and webhook sending use independent BackgroundServices. A blocked-sender test verifies evaluation does not await network completion.

## Deployment checks not replaced by automated tests

- Verify real Clerk issuer, JWT template audience, authorized parties, publishable key and hosted sign-in origins. Offline JWT tests use a local RSA key, not a real Clerk tenant.
- Verify outbound alert webhook DNS/TLS behavior and receiver deduplication in the deployment network. Unit tests deliberately do not contact external destinations.
- Review documented retention/storage limits for notifications and portal invitations, per-process login throttling, export snapshot limits and final-audit failure semantics.
- The Clerk SDK's lazy-loaded chunk produces a bundle-size warning. Existing NuGet/nullable warnings remain outside this feature scope.

See the individual feature documents for supported semantics and limitations. Passing this verification does not claim that every external provider or production configuration has been tested.

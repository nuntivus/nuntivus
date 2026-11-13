# Public-alpha publication readiness

## Scope

The approved Apache 2.0 publication remediation is implemented locally. This is
an evidence-based public-alpha assessment, not a production-security certification
or a legal clearance of contributor/asset ownership.

## Verified results

- Workspace build, clean-snapshot locked NuGet restore, Release API/Worker builds,
  and API/Worker publish succeed.
- 295 unit tests and 337 integration tests pass, including real-NATS duplicate
  subject binding/create/update and RabbitMQ exclusive-queue regressions.
- Frontend clean `npm ci`, lint, build, 29 non-browser tests, and 57 browser
  regressions pass. Browser regressions mock API boundaries.
- All nine project transitive NuGet audits and both frontend production/full
  audits report zero known vulnerabilities at validation time. This is not proof
  of vulnerability absence; a transitive CryptoJS deprecation remains an upstream
  maintenance consideration.
- Workflows pass actionlint 1.7.12. Local Markdown target checks and whitespace
  checks pass. Hosted GitHub workflows have not yet run.
- Redacted Gitleaks 8.30.1 checks pass for 92 locally available history commits
  and staged pending changes in a separate clean clone. Exceptions are limited
  to the exact deterministic development sample and one historical test identifier.
- Fresh disposable Compose startup applies migrations and reaches API/Worker
  readiness. Restart with persisted events/deliveries remains healthy.
- Real-container smoke verifies initial admin creation/audit, repeated-bootstrap
  refusal, login, anonymous administration denial, Source-token enforcement,
  idempotent ingestion, and Worker delivery to a controlled receiver.
- Disposable production startup confirms PostgreSQL/NATS have no host bindings,
  HTTP administration redirects to HTTPS, and explicitly trusted forwarded HTTPS
  reaches authentication. Real external proxy certificates were not tested.
- Windows PowerShell production backup/restore verifies file hashes, restores
  changed database data from clean SQL and the offline JetStream archive, and
  recovers readiness. This small synthetic check is not a large-data restore test.
- The documented local-image `upgrade -Build` path rebuilds locked images,
  updates the image tag, and recovers both services' readiness. Registry pulls
  and rollback across incompatible database migrations were not certified.
- Apache license files are present in both images and match both publish outputs.

The repeatable development-container smoke uses
`tests/deployment/compose.smoke.yml` to avoid host-port collisions and
`node tests/deployment/smoke.mjs` for the authenticated flow. Run it only against
an empty disposable database; it intentionally creates the initial administrator.

## Owner actions before publication

1. Confirm rights to all contributions/assets, the license change, and relevant
   third-party attribution/distribution requirements.
2. Enable hosted private vulnerability reporting, secret scanning/push protection,
   dependency alerts/review, branch protections, and least-privilege release rules
   where supported; repository files do not configure these settings.
3. Review all intended release branches/tags and artifacts for secrets. The local
   history scan covers available refs, not unknown/unfetched history.
4. Use independent runtime keys/passwords and a real trusted HTTPS proxy. Preserve
   the encryption key with backups; changing it alone cannot decrypt old secrets.
5. Validate actual provider credentials, TLS/networking, external side effects,
   operational capacity, and disaster recovery before a production deployment.

No changes were committed or pushed by the assistant. Publication and the manual
owner actions remain the repository owner's responsibility.

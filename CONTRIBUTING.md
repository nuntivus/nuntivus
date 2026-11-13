# Contributing

Nuntivus is in early development. Discuss substantial API, architecture, or schema
changes in an issue before implementation; small focused fixes are welcome.
There is no guaranteed review or support response time.

## Workflow

1. Read the [architecture](docs/architecture.md) and [development guide](docs/development.md).
2. Create a focused branch and add regression coverage for changed behavior.
3. Run backend builds/tests and dashboard build/lint/unit/browser tests as applicable.
4. Update affected documentation and intentionally regenerate dependency lockfiles.
5. Open a pull request describing the problem, changes, and exact validation results.

Do not mix a feature fix with unrelated cleanup or a framework migration.
Keep existing interfaces, source/destination boundaries, shared form infrastructure,
and background-service patterns. Do not disable checks to obtain a passing result.

## Migrations and security

Add forward-only, ordered SQL migrations; never modify an already-applied migration.
Test application, reapplication, and validation against a disposable PostgreSQL instance.
Never commit production credentials, customer payloads, dumps, or backup archives.
Report vulnerabilities privately according to [SECURITY.md](SECURITY.md).

## Licensing and provenance

Contributions intentionally submitted for inclusion are under [Apache License 2.0](LICENSE)
unless explicitly stated otherwise, consistent with its contribution terms.
Only submit code and assets you have the right to contribute. Preserve third-party
copyright/license/attribution notices and identify imported code or assets in the PR.
Do not claim a dependency is Apache-licensed simply because Nuntivus uses Apache 2.0.

Follow the [code of conduct](CODE_OF_CONDUCT.md).

# Nuntivus Server

Self-hosted customer event infrastructure for SaaS and platform teams.
Publish business events once; define how each customer receives them.

> **Early development — not yet production ready.** APIs, schemas, and configuration
> may change before the first stable release.

## Features

- Managed HTTP and pull Sources, with source-scoped ingestion idempotency.
- Customer contracts with shared destinations, filters, and delivery policies.
- HTTP, Kafka, RabbitMQ, NATS, and SQS delivery integrations.
- Schema validation, compatibility review, typed transformations, and dry runs.
- Delivery history, retries/replay, SLOs, alerts, and staff metadata exports.
- React administration dashboard and invitation-only read-only customer portal.

Processing is **at least once**; external consumers must tolerate duplicates.
See the [architecture](docs/architecture.md) and [feature limitations](docs/features/product-verification.md).

## Development quick-start

Requires Docker Compose with Linux containers. The default stack is for local
experimentation only and uses development credentials on loopback-bound ports.

```sh
git clone https://github.com/nuntivus/nuntivus-server.git
cd nuntivus-server
docker compose up --build -d
```

The stack runs PostgreSQL, NATS JetStream, migrations, the API, and Worker.
Check readiness at `http://localhost:8080/health/ready`; development Swagger is
available at `http://localhost:8080/swagger`.

### Dashboard

Requires Node.js 24. Run separately:

```sh
cd NuntivusServer.Dashboard
npm ci
npm run dev
```

Set `VITE_API_BASE_URL=http://localhost:8080` in the dashboard's `.env.local` when
using the Compose API. Open the URL printed by Vite. There is no pre-seeded admin
password: follow the [admin authentication guide](docs/self-hosting/admin-authentication.md)
to provision a local account or configure an identity provider.

### Ingest an event

Create a managed Source, then publish to `POST /v1/sources/{id}/events`.
A minimal event contains `idempotencyKey`, `type`, and a JSON object `payload`.
See [Source setup and envelope examples](docs/sources.md). The old direct event
publish and generic webhook routes are not supported.

## Production deployment

Do **not** deploy the development Compose file publicly.
Use [production configuration](docs/self-hosting/production-configuration.md)
and `docker-compose.prod.yml` with generated secrets and configured authentication.
PostgreSQL and NATS stay inside the container network; expose the API through a
trusted HTTPS reverse proxy.

Read the [operations guides](docs/self-hosting/README.md) for backups, restores,
upgrades, and deployment-specific acceptance checks.

## Development and tests

Projects target .NET 8; the solution uses the SDK pinned in `global.json`.
The dashboard is validated separately through npm.

See [development and validation](docs/development.md) for setup, builds, backend
tests, browser tests, dependency audits, and supported toolchains.

## Documentation

- [Documentation index](docs/README.md)
- [Concepts and architecture](docs/architecture.md)
- [Sources](docs/sources.md)
- [Delivery ordering guarantees](docs/delivery-ordering-guarantees.md)
- [Feature coverage and limitations](docs/features/product-verification.md)
- [Self-hosting operations](docs/self-hosting/README.md)

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.
Report vulnerabilities privately according to [SECURITY.md](SECURITY.md).

## License

Nuntivus Server is open source under the [Apache License 2.0](LICENSE).
Commercial use, modification, redistribution, and hosting are permitted subject
to its terms. Third-party dependencies and assets retain their applicable licenses.

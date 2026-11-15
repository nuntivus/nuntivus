# Production Configuration Guide

## 1. Prerequisites

- Docker Engine + Docker Compose plugin
- Linux host with persistent disk for Postgres and JetStream volumes
- TLS termination in front of the API (reverse proxy or ingress)

## 2. Stack files

- `docker-compose.prod.yml`: production stack
- `.env`: runtime variables (copy from `.env.example`)

Application images are built from this checkout, never pulled from a registry.
PostgreSQL, NATS and Docker build dependencies can still be downloaded.
For independent API/worker deployments and external infrastructure, see
[Compose deployment modes](compose-deployments.md).

```bash
cp .env.example .env
```

## 3. Required environment variables

At minimum configure:

- `NUNTIVUS_POSTGRES_DB`
- `NUNTIVUS_POSTGRES_USER`
- `NUNTIVUS_POSTGRES_PASSWORD`
- `NUNTIVUS_DESTINATION_CREDENTIALS_KEY` (base64, 32-byte key)
- `NUNTIVUS_ENVIRONMENT=Production`
- `NUNTIVUS_LOCAL_AUTH_SIGNING_KEY` (a separate base64 key, at least 32 bytes)

Generate independent keys using `openssl rand -base64 32`. There are no production
default secrets. Alternatively, set `NUNTIVUS_LOCAL_AUTH_ENABLED=false` and configure
both `NUNTIVUS_AUTHORITY` and `NUNTIVUS_AUDIENCE` for your identity provider.
See [admin authentication](admin-authentication.md) for initial account setup.

Optional:

- `NUNTIVUS_OTLP_ENDPOINT`
- `NUNTIVUS_API_PORT`
- `NUNTIVUS_SHUTDOWN_TIMEOUT_SECONDS`
- `NUNTIVUS_PROXY_IP` (the exact trusted reverse-proxy address observed by the API)
- `NUNTIVUS_HTTPS_PORT` (external HTTPS port, default 443)

The stock Compose stack sets `Database__PostgresConnectionString` consistently for
migrations, API, and Worker. For an external database, use
`docker-compose.external.yml` and set `NUNTIVUS_DATABASE_CONNECTION_STRING` for all
three services; setting only `ConnectionStrings__Postgres` does not override an
already populated `Database` setting.

## 4. Standardized runtime configuration

Nuntivus API and Worker now use typed configuration objects with startup validation.

Primary sections:

- `Database`
- `Nats`
- `Worker`
- `Delivery`
- `DeliveryDispatcher`
- `DeliveryOrdering`
- `RetentionCleanup`
- `EndpointSecurity`
- `DestinationCredentials`
- `Cache`
- `HostOptions`

Validation is executed at startup and fails fast when required values are missing or invalid.

### Environment variable mapping

In addition to standard `.NET` key mapping (`Section__Property`), Nuntivus maps these `NUNTIVUS_*` variables:

- `NUNTIVUS_DATABASE_CONNECTION_STRING` -> `Database:PostgresConnectionString`
- `NUNTIVUS_POSTGRES_CONNECTION_STRING` -> `Database:PostgresConnectionString`
- `NUNTIVUS_DESTINATION_CREDENTIALS_KEY` -> `DestinationCredentials:EncryptionKey`
- `NUNTIVUS_NATS_URL` -> `Nats:Url`
- `NUNTIVUS_NATS_STREAM` -> `Nats:Stream`
- `NUNTIVUS_NATS_SUBJECTS` -> `Nats:Subjects`
- `NUNTIVUS_OTLP_ENDPOINT` -> `OpenTelemetry:OtlpEndpoint`
- `NUNTIVUS_SHUTDOWN_TIMEOUT_SECONDS` -> `HostOptions:ShutdownTimeout`

### Safe production defaults

When running in `Production`, Nuntivus enforces safe HTTP destination defaults:

- `EndpointSecurity:AllowHttpScheme=false`
- `EndpointSecurity:AllowPrivateNetworkDestinations=false`
- `EndpointSecurity:AllowRedirects=false`

## 5. Persistence and volumes

The production compose stack uses named volumes:

- `nuntivus-postgres-data` -> PostgreSQL data directory
- `nuntivus-nats-data` -> JetStream file store

Verify they exist after startup:

```bash
docker volume ls | grep nuntivus-
```

## 6. Migration execution

`migrate` is a one-shot service in `docker-compose.prod.yml`.

Behavior:

1. waits for PostgreSQL readiness
2. runs `nuntivus-api --migrate` using the same migration runner used by API/worker startup validation
3. validates migration filenames (`NNN_description.sql`) and deterministic ordering
4. acquires a PostgreSQL advisory lock to prevent concurrent migration execution
5. applies pending migrations transactionally
6. records migration history in `schema_migrations` with version, checksum, apply time, and duration

The API and worker start only after `migrate` completes successfully.

API and worker startup default to migration validation in production. If schema history is invalid or pending migrations exist, startup fails fast.

Operational controls:

- `DatabaseMigrations__Mode` (`Disabled`, `Validate`, `Apply`)
- `DatabaseMigrations__MigrationsPath` (default `Migrations`)
- `NUNTIVUS_DATABASE_MIGRATIONS_MODE` and `NUNTIVUS_DATABASE_MIGRATIONS_PATH` env mappings

Contributor workflow for new migrations:

1. add a new forward-only SQL file in `migration/` using `NNN_description.sql`
2. never edit an already-applied migration file
3. validate with integration tests and a dry-run deployment before production rollout

## 7. Readiness and lifecycle

- API readiness: `GET /health/ready`
- API liveness: `GET /health/live`
- API and worker graceful shutdown timeout: `HostOptions__ShutdownTimeout`
- Compose `stop_grace_period` is set for API and worker

## 8. Start and validate

```bash
docker compose --env-file .env -f docker-compose.prod.yml up -d --build
```

Check service health:

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs migrate
curl http://localhost:${NUNTIVUS_API_PORT}/health/ready
```

## 9. Security baseline

- Do not commit `.env` with secrets
- Use unique credentials per environment
- Rotate database and identity-provider credentials through your secret manager.
- Preserve the destination encryption key with backups. Replacing it without
  re-encrypting existing managed secrets makes those secrets unreadable; do not
  treat key rotation as a configuration-only change.
- PostgreSQL, NATS client, and NATS monitoring have no host port publications.
  Only trusted containers should join their Compose network. External brokers
  require authentication and TLS; internal-only deployment is not a substitute
  for access controls on the Docker host.
- API port publication binds to loopback. Use an HTTPS reverse proxy and configure
  its exact observed IP in `NUNTIVUS_PROXY_IP`. Do not trust all forwarded headers.
  The API processes trusted forwarded headers before HTTPS redirection.
- Use container-local readiness checks rather than assuming host HTTP is HTTPS.
- Configure a non-localhost HTTPS dashboard API URL and approved origins at your
  reverse proxy when hosting the dashboard separately.

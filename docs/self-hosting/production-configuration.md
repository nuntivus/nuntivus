# Production Configuration Guide

## 1. Prerequisites

- Docker Engine + Docker Compose plugin
- Linux host with persistent disk for Postgres and JetStream volumes
- TLS termination in front of the API (reverse proxy or ingress)

## 2. Stack files

- `docker-compose.prod.yml`: production stack
- `.env`: runtime variables (copy from `.env.example`)

```bash
cp .env.example .env
```

## 3. Required environment variables

At minimum configure:

- `KAIRO_POSTGRES_DB`
- `KAIRO_POSTGRES_USER`
- `KAIRO_POSTGRES_PASSWORD`
- `KAIRO_DESTINATION_CREDENTIALS_KEY` (base64, 32-byte key)
- `KAIRO_ENVIRONMENT=Production`

Optional:

- `KAIRO_OTLP_ENDPOINT`
- `KAIRO_API_PORT`
- `KAIRO_SHUTDOWN_TIMEOUT_SECONDS`
- `KAIRO_DATABASE_CONNECTION_STRING` (alternative to compose-generated `ConnectionStrings__Postgres`)

## 4. Standardized runtime configuration

Kairo API and Worker now use typed configuration objects with startup validation.

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

In addition to standard `.NET` key mapping (`Section__Property`), Kairo maps these `KAIRO_*` variables:

- `KAIRO_DATABASE_CONNECTION_STRING` -> `Database:PostgresConnectionString`
- `KAIRO_POSTGRES_CONNECTION_STRING` -> `Database:PostgresConnectionString`
- `KAIRO_DESTINATION_CREDENTIALS_KEY` -> `DestinationCredentials:EncryptionKey`
- `KAIRO_NATS_URL` -> `Nats:Url`
- `KAIRO_NATS_STREAM` -> `Nats:Stream`
- `KAIRO_NATS_SUBJECTS` -> `Nats:Subjects`
- `KAIRO_OTLP_ENDPOINT` -> `OpenTelemetry:OtlpEndpoint`
- `KAIRO_SHUTDOWN_TIMEOUT_SECONDS` -> `HostOptions:ShutdownTimeout`

### Safe production defaults

When running in `Production`, Kairo enforces safe HTTP destination defaults:

- `EndpointSecurity:AllowHttpScheme=false`
- `EndpointSecurity:AllowPrivateNetworkDestinations=false`
- `EndpointSecurity:AllowRedirects=false`

## 5. Persistence and volumes

The production compose stack uses named volumes:

- `kairo-postgres-data` -> PostgreSQL data directory
- `kairo-nats-data` -> JetStream file store

Verify they exist after startup:

```bash
docker volume ls | grep kairo-
```

## 6. Migration execution

`migrate` is a one-shot service in `docker-compose.prod.yml`.

Behavior:

1. waits for PostgreSQL readiness
2. runs `kairo-api --migrate` using the same migration runner used by API/worker startup validation
3. validates migration filenames (`NNN_description.sql`) and deterministic ordering
4. acquires a PostgreSQL advisory lock to prevent concurrent migration execution
5. applies pending migrations transactionally
6. records migration history in `schema_migrations` with version, checksum, apply time, and duration

The API and worker start only after `migrate` completes successfully.

API and worker startup default to migration validation in production. If schema history is invalid or pending migrations exist, startup fails fast.

Operational controls:

- `DatabaseMigrations__Mode` (`Disabled`, `Validate`, `Apply`)
- `DatabaseMigrations__MigrationsPath` (default `Migrations`)
- `KAIRO_DATABASE_MIGRATIONS_MODE` and `KAIRO_DATABASE_MIGRATIONS_PATH` env mappings

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
curl http://localhost:${KAIRO_API_PORT}/health/ready
```

## 9. Security baseline

- Do not commit `.env` with secrets
- Use unique credentials per environment
- Rotate DB/NATS credentials and encryption keys through your secret manager
- Restrict host/network access to Postgres and NATS ports

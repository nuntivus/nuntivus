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

## 4. Persistence and volumes

The production compose stack uses named volumes:

- `kairo-postgres-data` -> PostgreSQL data directory
- `kairo-nats-data` -> JetStream file store

Verify they exist after startup:

```bash
docker volume ls | grep kairo-
```

## 5. Migration execution

`migrate` is a one-shot service in `docker-compose.prod.yml`.

Behavior:

1. waits for PostgreSQL readiness
2. creates `schema_migrations` table if missing
3. applies each `0*.sql` migration once
4. records applied file names

The API and worker start only after `migrate` completes successfully.

## 6. Readiness and lifecycle

- API readiness: `GET /health/ready`
- API liveness: `GET /health/live`
- API and worker graceful shutdown timeout: `HostOptions__ShutdownTimeout`
- Compose `stop_grace_period` is set for API and worker

## 7. Start and validate

```bash
docker compose --env-file .env -f docker-compose.prod.yml up -d --build
```

Check service health:

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs migrate
curl http://localhost:${KAIRO_API_PORT}/health/ready
```

## 8. Security baseline

- Do not commit `.env` with secrets
- Use unique credentials per environment
- Rotate DB/NATS credentials and encryption keys through your secret manager
- Restrict host/network access to Postgres and NATS ports

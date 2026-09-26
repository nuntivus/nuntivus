# Deployment Examples

## Example 1: Single VM Docker host

Use the production compose stack directly.

```bash
cp .env.example .env
# edit .env

docker compose --env-file .env -f docker-compose.prod.yml up -d --build
```

Recommended VM baseline:

- 2+ vCPU
- 4+ GB RAM
- SSD storage for persistent volumes

## Example 2: Reverse proxy + Nuntivus API

Expose only API through a reverse proxy (Nginx/Caddy/Traefik):

- proxy external HTTPS traffic to `nuntivus-api:8080`
- keep Postgres and NATS internal/private

## Example 3: Scale worker replicas

Increase worker concurrency while keeping API fixed:

```bash
docker compose --env-file .env -f docker-compose.prod.yml up -d --scale worker=3
```

`ContractMatcherWorker` uses a shared durable consumer, so JetStream can distribute workload across replicas.

## Example 4: External managed PostgreSQL

If using managed PostgreSQL:

- remove/disable `postgres` service in your deployment overlay
- set `ConnectionStrings__Postgres` to managed instance connection string
- keep `migrate` service (pointed to managed DB)

## Example 5: External NATS cluster

If using external NATS/JetStream:

- remove/disable `nats` service in your deployment overlay
- set `NUNTIVUS_NATS_URL` to external URL
- ensure stream name/subjects match worker configuration

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

- proxy external HTTPS traffic to `api:8080` on the Compose network, or to the
  loopback-published API port from a host proxy
- keep Postgres and NATS internal/private
- set `NUNTIVUS_PROXY_IP` to the proxy address actually observed inside the API
  container, and forward `X-Forwarded-Proto: https` and the real client address
- do not publish internal broker/database/monitor ports in an overlay

## Example 3: Scale worker replicas

Increase worker concurrency while keeping API fixed:

```bash
docker compose --env-file .env -f docker-compose.prod.yml up -d --scale worker=3
```

`ContractMatcherWorker` uses a shared durable consumer, so JetStream can distribute workload across replicas.

## Example 4: External managed PostgreSQL

If using managed PostgreSQL:

- remove/disable `postgres` service in your deployment overlay
- set `Database__PostgresConnectionString` to the managed instance connection
  string for migrate, API, and Worker; require TLS/certificate validation
- keep `migrate` service (pointed to managed DB)

## Example 5: External NATS cluster

If using external NATS/JetStream:

- remove/disable `nats` service in your deployment overlay
- set `NUNTIVUS_NATS_URL` to external URL
- ensure stream name/subjects match worker configuration

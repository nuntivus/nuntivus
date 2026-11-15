# Compose deployment modes

Requires a source checkout, Docker with Linux containers, and Compose 2.24.4+.
Application images are built locally. PostgreSQL, NATS, .NET/Node/nginx base images,
and package dependencies may be downloaded; these are not air-gapped builds.

## Local stack with automatic setup

From the repository root:

```sh
docker compose -f docker-compose.quickstart.yml up -d --wait
```

This builds API, worker and dashboard, generates secrets, applies migrations and
creates the first administrator. See the root README for login instructions.
It is a local Development stack, not a publicly exposed production deployment.

## Independent services on one host

For development, the existing launcher supports selecting either process:

```powershell
.\start.ps1 -Services api
.\start.ps1 -Services worker
```

Both commands use the same local project and infrastructure. Starting only the
API does not start the worker; events can be ingested, but background processing
waits until a worker is running. Starting the worker does not start the API.

For production, copy `.env.example` to `.env`, configure unique credentials and
keys as described in [production configuration](production-configuration.md),
and run from the same directory/project:

```sh
docker compose --env-file .env -f docker-compose.prod.yml up -d --build api
docker compose --env-file .env -f docker-compose.prod.yml up -d --build worker
```

Compose starts each service's infrastructure and migration dependencies without
starting the other application. Stop either application independently:

```sh
docker compose --env-file .env -f docker-compose.prod.yml stop worker
docker compose --env-file .env -f docker-compose.prod.yml stop api
```

The applications share PostgreSQL; API writes durable data/outbox records and the
worker performs asynchronous processing using PostgreSQL and NATS JetStream.
There is no worker-to-API HTTP endpoint to configure. Keep the worker's NATS URL,
stream and subjects consistent across worker hosts.

## Separate hosts or separately managed infrastructure

Recommended topology: one privately reachable PostgreSQL service, one persistent
NATS JetStream service, and independently deployed API and worker containers.
Use private DNS/VPN networking; Docker service names such as `postgres` and `nats`
do not resolve across Docker hosts. `localhost` inside a container means that
container, not the database host.

On each application host:

1. Check out the same application revision.
2. Copy `.env.example` to `.env` and configure the required production settings.
   Keep `NUNTIVUS_POSTGRES_DB`, `NUNTIVUS_POSTGRES_USER` and
   `NUNTIVUS_POSTGRES_PASSWORD` populated because the base file still declares the
   optional local database service.
3. Set `NUNTIVUS_DATABASE_CONNECTION_STRING` to the same external database on all
   hosts. It replaces the connection string for API, worker and migrations.
4. Use exactly the same `NUNTIVUS_DESTINATION_CREDENTIALS_KEY` everywhere. Preserve
   the key when moving an existing installation; generating a new key makes
   existing encrypted credentials unreadable.
5. Set `NUNTIVUS_NATS_URL` to the private broker address on worker hosts, with
   `NUNTIVUS_NATS_TOKEN` or `NUNTIVUS_NATS_USER`/`NUNTIVUS_NATS_PASSWORD` matching
   the broker's authentication configuration. Enable JetStream on the broker.

Example non-secret connection settings (replace credentials and hostnames):

```dotenv
NUNTIVUS_DATABASE_CONNECTION_STRING=Host=db.internal;Port=5432;Database=nuntivus;Username=nuntivus;Password=replace-me;SSL Mode=VerifyFull
NUNTIVUS_NATS_URL=nats://nats.internal:4222
```

Use a trusted private network/VPN or configure broker TLS for traffic across
untrusted networks. Mount private CA certificates when required. Never publish
unauthenticated PostgreSQL/NATS or NATS monitoring to the Internet. This override
connects to infrastructure; it does not provision its networking, TLS or accounts.

Before starting applications, run migrations once from one deployment host:

```sh
docker compose --env-file .env -f docker-compose.prod.yml -f docker-compose.external.yml build migrate
docker compose --env-file .env -f docker-compose.prod.yml -f docker-compose.external.yml run --rm --no-deps migrate
```

Proceed only if migrations succeed. For a fresh installation, provision an
administrator on the API host before accepting traffic:

```sh
docker compose --env-file .env -f docker-compose.prod.yml -f docker-compose.external.yml run --rm --no-deps -e AdminBootstrap__Username=admin api --create-admin
```

The API image was built by `build migrate` above. See
[admin authentication](admin-authentication.md) for password input and external
identity-provider alternatives.

On the API host:

```sh
docker compose --env-file .env -f docker-compose.prod.yml -f docker-compose.external.yml up -d --build api
```

On the worker host:

```sh
docker compose --env-file .env -f docker-compose.prod.yml -f docker-compose.external.yml up -d --build worker
```

The override removes local infrastructure/migration startup dependencies. Neither
command starts local PostgreSQL, NATS, or the other application. Startup still
validates the database schema, so migrations must already be applied. Always name
the desired service: running `up` without a service also selects the one-shot
migration service and both applications.

The API remains bound to host loopback. Put an HTTPS reverse proxy on that host
and configure its trusted address; the worker needs no published inbound port.
If the dashboard is hosted separately, point its browser-visible API URL at the
HTTPS API endpoint, not a Docker-internal hostname, and configure allowed origins.

## Builds and existing installations

Quick-start uses `pull_policy: build`, so plain `up` builds application images.
Development/production services use `pull_policy: never`: use `up --build` or
`build` before the first start. Later, `up --no-build` reuses existing local images.
External infrastructure images retain normal Docker pull behavior.

Keep the same Compose project name and named volumes when changing an existing
installation. Do not use `down --volumes` unless intentionally deleting all data.
For cross-host moves, transfer database/JetStream data and the existing encryption
key using the backup/restore guides; changing the connection string alone does
not move data.

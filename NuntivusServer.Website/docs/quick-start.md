---
title: Local quick start
---

Run the API, Worker and dashboard locally using Docker Engine/Desktop with Linux containers and Docker Compose **2.24.4 or newer**.

:::warning Local evaluation only
This stack runs in Development mode and publishes loopback-bound ports. Do not expose it publicly. See the [production configuration guide](reference/self-hosting/production-configuration.md) for deployment requirements.
:::

## Start from source

```bash
git clone https://github.com/nuntivus/nuntivus-server.git
cd nuntivus-server
docker compose -f docker-compose.quickstart.yml up -d --wait
```

Compose builds the application from the checkout, starts PostgreSQL and NATS, generates credentials and encryption keys, applies migrations and bootstraps the administrator. Base images and dependencies may be downloaded. Host installations of .NET and Node.js are not required for this workflow.

## Open the dashboard

Visit **http://localhost:5173** and sign in as **admin**. Retrieve the generated initial password locally:

```bash
docker compose -f docker-compose.quickstart.yml exec -T api cat /run/nuntivus-api/admin-password
```

Keep this password private. It is not a shared default. Existing administrators and secrets are preserved; the command retrieves the original bootstrap password, not a password you changed later.

## Explore the delivery flow

Create a customer, configure a Source and a Destination, then link them through a contract. Use connection tests and contract dry runs to inspect configuration before sending real events. Connection tests can perform real outbound operations: review their confirmation and documentation first.

## Stop without deleting data

```bash
docker compose -f docker-compose.quickstart.yml down
```

Do **not** add `--volumes` unless you intend to permanently delete the installation, including data and encryption keys. Back up the database, NATS data and secrets together.

Continue with [Sources](reference/sources.md), [architecture](reference/architecture.md) and [Compose deployment options](reference/self-hosting/compose-deployments.md).

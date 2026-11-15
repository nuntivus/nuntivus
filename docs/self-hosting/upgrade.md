# Upgrade Guide

## Existing installations after the project rename

The renamed Compose volumes, default PostgreSQL database, NATS stream and consumer names are new resources. Before deploying over an existing installation, back up and migrate the existing PostgreSQL and JetStream data (or keep pointing the services at the existing resources); otherwise the new stack will start with empty stores. Update `.env` to the `NUNTIVUS_*` names, and update clients that send the renamed event headers. Do not treat this rename as an image-tag-only upgrade.

## 1. Upgrade strategy

Use immutable image tags and upgrade in controlled steps:

1. backup Postgres + JetStream
2. update `NUNTIVUS_IMAGE_TAG` in `.env`
3. build new application images from the desired source revision
4. restart stack
5. validate readiness and worker processing

## Fast path (recommended)

From repository root:

```powershell
./scripts/ops.ps1 upgrade -Tag <image-tag> -Build
```

Script behavior:

- runs pre-upgrade backup by default
- updates `NUNTIVUS_IMAGE_TAG` in `.env`
- builds local application images and deploys compose services
- validates API readiness
- rolls back to previous tag if health validation fails

Use `-SkipBackup` only when you have a verified recent backup.
Check out the desired source revision first and use `-Build`. Application tags
identify local builds, not published registry releases. Keep previous tagged local
images available for rollback.

## 2. Upgrade command

```bash
docker compose --env-file .env -f docker-compose.prod.yml build

docker compose --env-file .env -f docker-compose.prod.yml up -d
```

The migration service runs before API/worker startup and applies only unapplied SQL files.

Migration history is checksum-aware. If an already-applied migration file was modified, validation fails and rollout stops.

## 3. Validation checklist

- `docker compose -f docker-compose.prod.yml ps`
- `docker compose -f docker-compose.prod.yml logs migrate`
- API `GET /health/ready` is healthy
- worker logs show consumers running

## 4. Rollback

If validation fails:

1. set previous `NUNTIVUS_IMAGE_TAG`
2. redeploy with `up -d`
3. restore DB/JetStream from backups if migrations introduced irreversible changes

Rollback decision guide:

- **No migration applied**: roll back image tag only.
- **Only reversible operational issue (app bug/config)**: roll back image tag, keep DB as-is.
- **Schema/data migration applied and behavior is unsafe**: restore Postgres/JetStream from pre-upgrade backups and redeploy previous tag.
- **Checksum mismatch error**: do not patch old migration files; create a new corrective migration and re-run upgrade pipeline.

## 5. Migration discipline

- only add forward-ordered SQL files (e.g., `014_*.sql`)
- never modify already-applied migration files
- include migration integration tests for apply/idempotency/validation paths
- test upgrade path in staging before production rollout

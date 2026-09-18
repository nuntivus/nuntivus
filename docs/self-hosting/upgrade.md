# Upgrade Guide

## 1. Upgrade strategy

Use immutable image tags and upgrade in controlled steps:

1. backup Postgres + JetStream
2. update `KAIRO_IMAGE_TAG` in `.env`
3. pull/build new images
4. restart stack
5. validate readiness and worker processing

## Fast path (recommended)

From repository root:

```powershell
./scripts/ops.ps1 upgrade -Tag <image-tag>
```

Script behavior:

- runs pre-upgrade backup by default
- updates `KAIRO_IMAGE_TAG` in `.env`
- pulls + deploys compose services
- validates API readiness
- rolls back to previous tag if health validation fails

Use `-SkipBackup` only when you have a verified recent backup.

## 2. Upgrade command

```bash
docker compose --env-file .env -f docker-compose.prod.yml pull
# or build locally
# docker compose --env-file .env -f docker-compose.prod.yml build

docker compose --env-file .env -f docker-compose.prod.yml up -d
```

The migration service runs before API/worker startup and applies only unapplied SQL files.

## 3. Validation checklist

- `docker compose -f docker-compose.prod.yml ps`
- `docker compose -f docker-compose.prod.yml logs migrate`
- API `GET /health/ready` is healthy
- worker logs show consumers running

## 4. Rollback

If validation fails:

1. set previous `KAIRO_IMAGE_TAG`
2. redeploy with `up -d`
3. restore DB/JetStream from backups if migrations introduced irreversible changes

## 5. Migration discipline

- only add forward-ordered SQL files (e.g., `014_*.sql`)
- never modify already-applied migration files
- test upgrade path in staging before production rollout

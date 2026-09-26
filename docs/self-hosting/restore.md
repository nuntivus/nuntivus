# Restore Guide

Use this guide to restore Nuntivus from PostgreSQL and JetStream backups.

## Fast path (recommended)

From repository root:

```powershell
./scripts/ops.ps1 restore -DbDumpPath backups/<timestamp>/nuntivus-postgres-<timestamp>.sql -JetStreamDumpPath backups/<timestamp>/nuntivus-jetstream-<timestamp>.tgz
```

The script stops dependent services, restores Postgres and JetStream, restarts the stack, and waits for API readiness.

## 1. Stop application services

```bash
docker compose --env-file .env -f docker-compose.prod.yml stop api worker
```

## 2. Restore PostgreSQL

Replace `<dump-file>` with your backup file.

```bash
cat <dump-file> | docker compose --env-file .env -f docker-compose.prod.yml exec -T postgres \
  pg_restore -U "$NUNTIVUS_POSTGRES_USER" -d "$NUNTIVUS_POSTGRES_DB" --clean --if-exists --no-owner --no-privileges
```

## 3. Restore JetStream volume

1. stop NATS service
2. clear existing data
3. extract backup archive

```bash
docker compose --env-file .env -f docker-compose.prod.yml stop nats

docker run --rm -v nuntivus-nats-data:/data alpine sh -c 'rm -rf /data/*'

docker run --rm \
  -v nuntivus-nats-data:/data \
  -v "${PWD}:/backup" \
  alpine sh -c 'cd /data && tar xzf /backup/<jetstream-backup-file>.tgz'
```

## 4. Restart stack

```bash
docker compose --env-file .env -f docker-compose.prod.yml up -d
```

## 5. Post-restore validation

- API readiness returns healthy
  - `GET /health/ready`
- migrations service completed successfully
- worker logs show NATS/JetStream bootstrap completion
- expected events/contracts/customers are present

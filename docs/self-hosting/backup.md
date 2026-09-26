# Backup Guide

This guide covers PostgreSQL and JetStream data backups for the Docker Compose production stack.

## Fast path (recommended)

From repository root:

```powershell
./scripts/ops.ps1 backup
```

Output:

- timestamped backup directory in `backups/`
- PostgreSQL SQL dump
- JetStream volume archive
- `manifest.json` (future dashboard-compatible metadata)

## 1. What to back up

- PostgreSQL logical backup (`pg_dump`)
- JetStream file store (`nuntivus-nats-data` volume)
- `.env` (stored securely outside the host)

## 2. PostgreSQL backup

Run from the repository root:

```bash
mkdir -p backups

docker compose --env-file .env -f docker-compose.prod.yml exec -T postgres \
  pg_dump -U "$NUNTIVUS_POSTGRES_USER" -d "$NUNTIVUS_POSTGRES_DB" -Fc > "backups/nuntivus-postgres-$(date +%F-%H%M%S).dump"
```

## 3. JetStream backup (volume archive)

```bash
mkdir -p backups

docker run --rm \
  -v nuntivus-nats-data:/data \
  -v "${PWD}/backups:/backup" \
  alpine sh -c 'cd /data && tar czf /backup/nuntivus-jetstream-$(date +%F-%H%M%S).tgz .'
```

## 4. Backup verification

- confirm backup files are non-empty
- periodically restore backups in a staging environment
- keep checksum manifests for integrity checks

## 5. Recommended schedule

- PostgreSQL: at least daily, more frequently for low-RPO needs
- JetStream: daily plus before upgrades
- Retention: keep according to compliance policy

# Restore guide

Restore only trusted backups into the intended deployment. This operation replaces
application database and JetStream state; validate backups in staging first.

From the repository root:

```powershell
./scripts/ops.ps1 restore -DbDumpPath backups/<timestamp>/nuntivus-postgres-<timestamp>.sql -JetStreamDumpPath backups/<timestamp>/nuntivus-jetstream-<timestamp>.tgz
```

Use `-ComposeFile` and `-EnvFile` when working with an overlay. The supported script
format is plain SQL from `ops.ps1 backup`, not a custom-format `pg_dump -Fc` file.
Verify the manifest hashes before restoration and restore the original destination
credential encryption key through your secret manager.

The script stops API, Worker, and NATS; executes SQL with `ON_ERROR_STOP`; clears
and restores the actual Compose NATS volume; restarts the stack; and checks API
readiness inside its container. PostgreSQL must remain running. No internal-service
host ports are required. If restoration fails, dependent services stay stopped;
investigate before bringing an inconsistent snapshot online.

## Post-restore checks

- API and Worker container health checks pass.
- Migration validation accepts the restored history.
- Worker logs show JetStream consumers running.
- Expected customers, contracts, events, and delivery state are present.
- Managed credentials decrypt with the restored key.
- A test Source event is accepted and delivered to a controlled receiver.

Never extract untrusted archives into a database or broker volume. Keep a verified
pre-restore backup and use your deployment's recovery procedures for failure.

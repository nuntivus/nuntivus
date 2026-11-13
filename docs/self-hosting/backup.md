# Backup guide

Back up PostgreSQL, JetStream, and the destination-credential encryption key.
Keep runtime secrets in a secure secret manager, not beside public artifacts.

From the repository root:

```powershell
./scripts/ops.ps1 backup
```

Use `-ComposeFile` and `-EnvFile` for a deployment overlay. The script stops API,
Worker, and NATS while taking a consistent offline snapshot, then starts them
again in a `finally` block and checks readiness inside the API container. Plan
for this maintenance window; externally queued messages may accumulate upstream.

The output directory under `backups/` contains:

- A plain SQL dump with clean/if-exists statements.
- A JetStream archive from the actual Compose-managed NATS volume.
- A manifest with file sizes and SHA-256 hashes.

No PostgreSQL or NATS host ports are needed. The script uses `docker compose exec`
and a temporary archive container. A single NATS container with a named `/data`
volume is required; scaled/clustered brokers need provider-supported backups.

Verify nonempty files and hashes, restrict backup access, and periodically restore
into an isolated staging deployment. Backup files can contain sensitive event
and customer data. Keep retention aligned with your compliance requirements.
Do not replace the encryption key without re-encrypting existing managed secrets.

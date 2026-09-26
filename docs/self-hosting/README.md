# Self-Hosting Operations

This directory contains production self-hosting operational guides for Nuntivus.

Quick automation script:

- `scripts/ops.ps1`
  - `./scripts/ops.ps1 backup`
  - `./scripts/ops.ps1 restore -DbDumpPath <db.sql> -JetStreamDumpPath <jetstream.tgz>`
  - `./scripts/ops.ps1 upgrade -Tag <image-tag>`

The script also writes a backup `manifest.json` designed to stay compatible with future dashboard-based operations.

- [Production configuration guide](./production-configuration.md)
- [Admin authentication and RBAC](./admin-authentication.md)
- [Backup guide](./backup.md)
- [Restore guide](./restore.md)
- [Upgrade guide](./upgrade.md)
- [Deployment examples](./deployment-examples.md)

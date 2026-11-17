# Upgrade Options — NuntivusServer

Assessment: 10 SDK-style projects, modern .NET targets, 8 package compatibility/update findings, and flagged API changes.

## Strategy

### Upgrade Strategy
A coordinated upgrade keeps the small, closely related solution on one framework.

| Value | Description |
|-------|-------------|
| **All-at-Once** (selected) | Upgrade all projects together in one atomic pass. |
| Top-Down | Upgrade applications first using temporary library multi-targeting. |

## Compatibility

### Unsupported Packages
The assessment flags container tooling references in the API and Worker as incompatible.

| Value | Description |
|-------|-------------|
| **Resolve Inline** (selected) | Research and resolve incompatible references within the upgrade task. |
| Defer Resolution | Use temporary stubs and follow-up resolution tasks. |
| Compatibility Mode | Retain incompatible dependencies with compatibility support; runtime risks remain. |

### Unsupported API Handling
The assessment flags binary and source compatibility changes.

| Value | Description |
|-------|-------------|
| **Fix Inline** (selected) | Resolve API changes during the upgrade without deferred stubs. |
| Defer Complex Changes | Fix mechanical changes now and defer complex replacements to subtasks. |

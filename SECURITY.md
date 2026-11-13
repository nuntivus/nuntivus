# Security policy

## Status and reporting

Nuntivus is an early-development project, not a production-security certification.
There is no stable-version support matrix or guaranteed security response time yet.

Use GitHub's private vulnerability reporting for this repository when enabled.
If unavailable, ask maintainers for a private contact channel without publishing
exploit details or credentials. Do not disclose vulnerabilities in public issues.
Include affected revisions, reproduction steps, impact, and proposed mitigations;
redact passwords, tokens, signing/encryption keys, and customer payloads.

## Deployment requirements

- Never expose the development Compose stack publicly or reuse development keys.
- Generate independent destination-encryption and administration signing keys.
- Keep PostgreSQL/NATS/monitoring inside the production container network.
- Restrict Docker host/network membership and use authentication/TLS for remote services.
- Terminate API HTTPS at a trusted proxy; accept forwarded headers only from configured IPs.
- Session-storage bearer tokens remain XSS-readable: serve trusted frontend assets,
  avoid untrusted scripts, and set a deployment-appropriate content-security policy.
- Request limits are per process; use gateway/distributed controls where needed.
- Back up encryption keys securely. A key replacement alone does not re-encrypt secrets.

See [production configuration](docs/self-hosting/production-configuration.md).

## Before public publication

Scan the full available Git history, not only the latest files. From the repository root:

```powershell
docker run --rm -v "${PWD}:/repo:ro" -w /repo -e GIT_CONFIG_COUNT=1 -e GIT_CONFIG_KEY_0=safe.directory -e GIT_CONFIG_VALUE_0=/repo ghcr.io/gitleaks/gitleaks@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f git --log-opts=--all --redact --no-banner
```

This checks locally available refs; verify that all intended branches/tags were fetched.
Also review the pending working tree and release artifacts. Scanner success is not
proof that no secrets or security defects exist. False positives need narrow,
evidence-based exceptions; do not exclude entire test/config directories.
The checked-in exceptions match only the exact deterministic development key
in development configuration files and one historical test-idempotency fingerprint.

For real credentials, revoke/rotate them at their provider, update runtime secret
configuration, and assess exposure. Deleting a file does not invalidate a credential.
Do not paste findings into public issues or automatically rewrite shared Git history.
History cleanup requires owner coordination and does not replace rotation.

## Manual GitHub settings

Repository administrators should enable, where available:

- Private vulnerability reporting.
- Secret scanning and push protection.
- Dependency graph, security alerts, and dependency review.
- Branch protection/rulesets requiring CI and Security checks with reviewed changes.
- Least-privilege workflow permissions and restricted release permissions.

The repository files do not themselves enable these hosted settings.
Before release, the owner must confirm rights to contributed code/assets and all
third-party attribution/distribution requirements.

# Publishing a clean public repository

Use a separate directory and a fresh initial commit on `master`. Do not delete
the original `.git` directory, rewrite its history, or change its remote.
Publishing the source publicly is separate from deploying it publicly: the
development and quick-start stacks must remain local only.

## 1. Review what will be published

- Confirm ownership of contributed code/assets and keep the Apache 2.0 license
  and required third-party notices. Starting fresh does not remove obligations.
- Review configuration, HTTP examples, fixtures, documentation, and images for
  credentials, private endpoints, personal data, and customer payloads.
- Keep source, tests, SQL migrations, lockfiles, Docker configuration, and CI.
  The existing project folders are application layers, not generated clutter.
- Do not copy `.vs`, `bin`, `obj`, `node_modules`, `dist`, test results, logs,
  `.env`, user settings, database dumps, or backups. Ignore rules cover these.
- The committed development credentials and deterministic sample keys are
  local-only examples. Never reuse them in production.

## 2. Export a clean working copy

From the original repository root, using PowerShell 5.1+ and Git:

```powershell
.\scripts\export-public-repository.ps1 -Destination ..\nuntivus-public -AdditionalPaths scripts/export-public-repository.ps1,tests/deployment/export-public-repository.test.ps1
```

The destination must not exist and must be outside the original repository.
The helper copies **tracked files using their current working-tree contents**,
including uncommitted edits. It copies no history, branches, tags, or remotes,
and changes neither the original index nor its working files. Paths matching
ignore rules and symbolic links/junctions are refused, not silently published.
Stage intended file deletions before exporting.

Untracked files are excluded unless named in `-AdditionalPaths`. The command
above explicitly includes the new helper and its test even before they are
tracked. Add any other intended new files explicitly after reviewing them.
Once all intended files are tracked, `-AdditionalPaths` can be omitted.
Do not copy the whole old directory over the export afterward.

## 3. Scan and validate the clean copy

```powershell
Set-Location ..\nuntivus-public
docker run --rm -v "${PWD}:/repo:ro" -w /repo ghcr.io/gitleaks/gitleaks@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f dir . --redact --no-banner
node scripts/check-doc-links.mjs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/deployment/export-public-repository.test.ps1
```

The helper's test uses a disposable Git fixture; it does not initialize the
export itself. On other platforms, use `pwsh -File` for the PowerShell test.
Run the backend, dashboard, and deployment checks in
[development and validation](development.md) before publishing. Do not rely on
old local test counts as evidence for a new release. Review all scanner findings;
scanner success alone does not prove that the directory contains no secrets.

The Docker quick-start still uses the image names and version declared in
`docker-compose.quickstart.yml`. A new GitHub repository does not publish or
transfer those images; verify availability and ownership before advertising them.

## 4. Create the first commit on master

Run these commands **only in the exported directory**, after review:

```powershell
git init -b master
git add .
git diff --cached --stat
git diff --cached --check
git diff --cached
```

Review the staged content locally; do not paste sensitive findings into issues.
When satisfied:

```powershell
git commit -m "Initial public release"
git log --oneline
```

The new history should contain exactly this initial commit. Run the full-history
scanner from the [security policy](../SECURITY.md) in this new repository too.
If real credentials are found, revoke/rotate them even though the old history
is not being published. Remove them before creating the public commit.

## 5. Publish to an empty GitHub repository

Create a new repository under the intended GitHub account or organization.
Select **Public**. Leave initialization with a README, license, or `.gitignore`
unchecked because the clean copy already contains them.

Replace the placeholder below with the new repository's HTTPS URL:

```powershell
git remote add origin https://github.com/YOUR-OWNER/YOUR-NEW-REPOSITORY.git
git push -u origin master
```

Do not use a mirror push, `--all`, `--tags`, or a force push from the original
repository. In GitHub settings, confirm that `master` is the default branch,
check that CI and Security workflows pass, and enable the hosted protections
listed in [SECURITY.md](../SECURITY.md), including private vulnerability reporting,
secret scanning/push protection, and branch rules where available.

No export or validation command publishes anything automatically. Repository
creation, owner settings, final content approval, and pushing remain manual.

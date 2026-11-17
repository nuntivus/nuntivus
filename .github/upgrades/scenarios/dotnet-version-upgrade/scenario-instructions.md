# .NET 10 Upgrade

## Preferences
- **Flow Mode**: Automatic
- **Commit Strategy**: Single Commit at End
- **Target Framework**: net10.0
- **Scope**: Entire NuntivusServer.slnx solution

## Source Control
- **Source Branch**: feat/net10
- **Working Branch**: upgrade-dotnet-10
- **Commit Strategy**: After Each Task
- **Branch Sync**: Auto (Merge)

## Key Decisions Log
- User confirmed .NET 10, solution scope, automatic execution, new upgrade branch, and after-each-task commits.

## Upgrade Options
**Source**: .github/upgrades/scenarios/dotnet-version-upgrade/upgrade-options.md

### Strategy
- Upgrade Strategy: All-at-Once

### Compatibility
- Unsupported Packages: Resolve Inline
- Unsupported API Handling: Fix Inline

## Decisions
- User confirmed the selected upgrade options; resolve compatibility findings inline without stubs.
- All-at-once planning recommends one final atomic commit; this supersedes the initialization commit cadence.
- User will start Docker and requests the container-backed tests be rerun before finalization; do not finalize using the prior Docker-unavailable results.

## Strategy
**Selected**: All-at-Once
**Rationale**: Modern SDK-style solution with a focused framework upgrade and no legacy Framework migration.

### Execution Constraints
- Update all C# targets and framework-coupled packages together before full solution validation.
- Resolve incompatible references and breaking APIs inline; no temporary stubs.
- Keep dashboard JavaScript tooling and unrelated code modernization outside scope.
- Validate the full solution build and affected tests; fix warnings without suppressions.
- Commit the completed atomic upgrade at the end; preserve the initialization source-control record.

## Build Tool Decisions
- **NuntivusServer.slnx**: dotnet build (SDK-style backend and supported JavaScript IDE integration); IDE run_build could not execute in this session.
- **Backend test projects**: dotnet test, run serially; Docker Engine with Linux containers is required for integration fixtures.

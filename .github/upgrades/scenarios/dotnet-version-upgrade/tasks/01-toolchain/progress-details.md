# Toolchain and baseline results

- .NET SDKs 10.0.103 and 10.0.401 installed. global.json pins stable SDK 10.0.401, already compatible; no change needed.
- Full solution CLI build succeeded on net8.0 with 0 errors and 23 existing warnings (nullable flow, obsolete Testcontainers constructors, and xUnit assertions). These are recorded for correction in the upgrade task; no application projects changed in this prerequisite task.
- Baseline unit tests: 320 passed, 0 failed, 0 skipped.
- Docker Desktop Linux engine unavailable (missing dockerDesktopLinuxEngine named pipe). Container-backed integration baseline cannot be validated until Docker is running.
- IDE build tool failed to execute without diagnostic information; CLI solution build captured usable output instead.
- Dashboard is a JavaScript SDK project with no explicit TFM; preserve it unchanged.
- Update inventory: nine C# TFMs, six framework-coupled direct package references, two runtime Docker image tags, CI runtime installation, development prerequisites, and package lock files.
- Preserve historical performance baseline version records. global.json and Docker SDK image pins already match 10.0.401.

## Evidence
- Build log: temporary session file nuntivus-baseline-build.log.
- Unit log: temporary session file nuntivus-baseline-unit.log.
- All prerequisite done-when criteria verified; integration environment limitation documented rather than reported as passing.

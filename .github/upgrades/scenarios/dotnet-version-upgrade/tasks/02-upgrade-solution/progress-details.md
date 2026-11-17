# .NET 10 upgrade changes

## Implemented
- Retargeted all nine C# projects from net8.0 to net10.0; preserved the JavaScript dashboard SDK project unchanged.
- Updated JwtBearer, Mvc.Testing, Caching.Memory, HealthChecks.Abstractions, and Http to 10.0.15. Removed Worker Extensions.Hosting after NU1510 confirmed it is supplied by the existing Microsoft.AspNetCore.App shared framework.
- Regenerated all nine NuGet package lockfiles using restore. Preserved compatible provider packages and xUnit 2.9.3 (security-maintained; assessment suggests the same version).
- Preserved Visual Studio container build targets: its net472 tooling metadata caused the runtime compatibility warning, but .NET 10 restore and build succeeded with the build-only package.
- Updated both Docker runtime images to aspnet:10.0 and CI runtime installation to 10.0.x. SDK 10.0.401 pin and build images were already correct.
- Updated README target badge and current development/architecture instructions; preserved historical performance measurement versions.
- Replaced obsolete KnownNetworks with KnownIPNetworks without changing proxy trust configuration.
- Added namespaced ApiAssemblyMarker and selected it in six WebApplicationFactory usages to resolve duplicate API/Worker Program types (CS0433).
- Fixed nullable flow in URI validation, HTTP credential rotation, NATS metadata, API request logging, and test diagnostics.
- Updated obsolete Testcontainers image constructors and xUnit assertions; consumed awaited Task.WhenAll results rather than blocking Task.Result calls. No warning suppression added.

## Validation
- Full solution build: succeeded, 0 warnings, 0 errors.
- Unit tests: 320 passed, 0 failed.
- Integration suite attempted: 12 passed, 374 failed, 386 total. Parsed TRX: all 374 failures are DockerUnavailableException during setup; no non-Docker failures. Docker Desktop Linux engine is not running. These tests are not reported as passing.
- Documentation tests: 3 passed. Link check: 44 Markdown files, 0 missing local targets (before final artifact generation).
- git diff --check: passed.

## Done-when verification
All targets, runtime/deployment changes, restore, warning-free build, and unit tests verified. Container-backed test execution is explicitly limited by the external Docker prerequisite. Final validation will audit packages, locked restore, and review the completed diff.

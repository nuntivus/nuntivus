# 01-toolchain: Verify toolchain and baseline

Verify the installed .NET 10 SDK and any global.json constraints. Inventory build and deployment settings, establish the existing build/test baseline, and identify external test prerequisites for the API, Worker, and test projects. The assessment flags the dashboard as net6.0; inspect its actual JavaScript SDK configuration before deciding whether any change is needed.

**Done when**: SDK compatibility is verified, framework-related build/deployment settings are inventoried, and baseline build/test results or environmental blockers are documented.

## Research
- Compatible .NET 10 SDK is installed; global.json already selects a compatible SDK and requires no edit.
- All nine C# projects target net8.0. Both Dockerfiles already use SDK 10.0.401 but retain aspnet:8.0 runtime images. CI installs 8.0.x; docs/development.md requires .NET 8 runtimes.
- Dashboard uses Microsoft.VisualStudio.JavaScript.Sdk and has no explicit TargetFramework; do not retarget it based on the assessment's inferred net6.0.
- Repository build guidance in docs/development.md requests serial builds/tests and portable per-project CLI validation for the .esproj solution.
- IDE build tool cannot execute in this session; use captured CLI builds. Docker Desktop Linux engine is unavailable; container-backed integration tests may be environmentally blocked.
- Historical tests/performance/local-baseline.md records measured .NET 8 results; preserve historical facts.

## Scope Inventory
- No application project changes in this prerequisite task; scope is toolchain, baseline evidence, and workflow documentation.
- Baseline validation covers the full solution and both test projects. Framework modifications remain in 02-upgrade-solution.

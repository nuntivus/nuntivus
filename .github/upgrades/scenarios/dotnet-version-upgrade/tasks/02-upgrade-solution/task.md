# 02-upgrade-solution: Upgrade projects and runtime configuration to .NET 10

Upgrade all nine C# project targets together, align framework-coupled package references, and update relevant container, CI, SDK, and documentation references. Research the container tooling compatibility finding and deprecated xUnit references, preserving compatible dependencies unless a change is needed. Inspect actual project references and package versions before acting on assessment suggestions.

Resolve build-breaking APIs inline and test the flagged authentication, JSON, HTTP, URI, TimeSpan, and Worker hosting behavior with existing tests. Keep the dashboard's JavaScript tooling unchanged unless integration with the new backend requires a targeted correction; do not perform unrelated npm or syntax modernization.

**Done when**: All C# projects target net10.0, runtime/deployment references are aligned, dependencies restore, the solution builds without errors or warnings, and affected tests pass or genuine external blockers are documented.

## Scope Inventory and Research
- Projects affected: Api, Application, Common, Contracts, Domain, Infastructure, Worker, UnitTests, IntegrationTests. Dashboard has no explicit TFM and remains unchanged.
- Import/dependency discovery confirms per-project TargetFramework and PackageReference definitions, without local imported targets or central package management. Keep existing project structure.
- Assessment per-project summaries and issue lists reviewed: Common/Contracts/Domain require TFM only; Application has 57 API flags; Infrastructure 74; API 65; Worker 36; UnitTests 27; IntegrationTests 396. No legacy framework technologies were detected.
- API changes include KnownNetworks obsolescence, ConfigurationBinder recompilation, TimeSpan overload resolution, JWT authentication, JsonDocument, HTTP content, URI, and ActivitySource behavior. Recompile and exercise tests; avoid speculative behavioral rewrites.
- Update JwtBearer, Mvc.Testing, Extensions.Hosting, Caching.Memory, Http, and HealthChecks.Abstractions to assessment-recommended 10.0.15.
- Container tooling nuspec describes Visual Studio build targets with net472 tooling dependencies, not runtime application code. The compatibility classifier cannot find a runtime TFM. Preserve IDE tooling functionality and validate restore/build rather than removing it blindly.
- xunit 2.9.3 is security-maintained; the assessment recommends the same version. Preserve the supported existing tests rather than introducing an unrelated xUnit v3 migration.
- global.json already pins SDK 10.0.401. Update aspnet runtime images to 10.0, CI runtime to 10.0.x, and docs/development.md prerequisites. Restore regenerates committed packages.lock.json files.
- Baseline warnings require nullable-flow corrections, Testcontainers image constructors, and Assert.Single predicate overloads. No warning suppression permitted.
- No STUB markers found. The selected all-at-once strategy explicitly supersedes generic tier-by-tier decomposition for modern-to-modern retargeting. Keep the coordinated TFM/package update atomic; no incompatible package replacement or stub chain is required.
- Language scope: use the target framework's default C# version; no unrelated syntax or architectural modernization.
- Docker Desktop is stopped; record container test limitations explicitly.
- First .NET 10 build: NU1510 identifies Worker Hosting reference as redundant with Microsoft.AspNetCore.App; remove it rather than suppressing the warning. ASPDEPR005 replaces KnownNetworks with KnownIPNetworks.
- CS0433: API and Worker both expose Program in the upgraded build. Introduce a dedicated namespaced API assembly marker and use it in WebApplicationFactory fixtures, avoiding ambiguous global entry-point types while still selecting the same application assembly.

# .NET 10 Upgrade Plan

## Overview

**Target**: net10.0
**Scope**: 9 .NET projects and the JavaScript dashboard project in NuntivusServer.slnx; approximately 51k assessed lines of code.

### Selected Strategy
**All-At-Once** — All projects upgraded simultaneously in a single operation.
**Rationale**: Small SDK-style solution on modern .NET with closely related applications and libraries; no Framework migration required.

Projects in one upgrade group: NuntivusServer.Api, NuntivusServer.Application, NuntivusServer.Common, NuntivusServer.Contracts, NuntivusServer.Domain, NuntivusServer.Infastructure, NuntivusServer.Worker, NuntivusServer.UnitTests, NuntivusServer.IntegrationTests, and NuntivusServer.Dashboard (JavaScript tooling; verify rather than assign an artificial .NET target).

## Tasks

### 01-toolchain: Verify toolchain and baseline

Verify the installed .NET 10 SDK and any global.json constraints. Inventory build and deployment settings, establish the existing build/test baseline, and identify external test prerequisites for the API, Worker, and test projects. The assessment flags the dashboard as net6.0; inspect its actual JavaScript SDK configuration before deciding whether any change is needed.

**Done when**: SDK compatibility is verified, framework-related build/deployment settings are inventoried, and baseline build/test results or environmental blockers are documented.

---

### 02-upgrade-solution: Upgrade projects and runtime configuration to .NET 10

Upgrade all nine C# project targets together, align framework-coupled package references, and update relevant container, CI, SDK, and documentation references. Research the container tooling compatibility finding and deprecated xUnit references, preserving compatible dependencies unless a change is needed. Inspect actual project references and package versions before acting on assessment suggestions.

Resolve build-breaking APIs inline and test the flagged authentication, JSON, HTTP, URI, TimeSpan, and Worker hosting behavior with existing tests. Keep the dashboard's JavaScript tooling unchanged unless integration with the new backend requires a targeted correction; do not perform unrelated npm or syntax modernization.

**Done when**: All C# projects target net10.0, runtime/deployment references are aligned, dependencies restore, the solution builds without errors or warnings, and affected tests pass or genuine external blockers are documented.

---

### 03-validation: Validate the complete upgraded solution

Verify the full solution build and run the unit and integration test suites on .NET 10. Audit the resolved dependency graph for vulnerabilities and remaining actionable framework/package conflicts, and check tracked files for stale .NET 8 build or deployment instructions.

Document external service limitations explicitly rather than treating unexecuted tests as passing. Record the final validation evidence and any deferred recommendations without expanding the scope to unrelated modernization.

**Done when**: Full solution build is warning-free, test outcomes and environmental limitations are recorded, package audit is reviewed, and no unintended old runtime targets remain.

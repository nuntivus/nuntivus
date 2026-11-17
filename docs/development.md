# Development and validation

## Prerequisites

- The .NET 10 SDK selected by `global.json`; backend projects target .NET 10.
- .NET 10 and ASP.NET Core 10 runtimes for backend tests.
- Node.js 24 and npm, plus Playwright Chromium for browser tests.
- Docker Engine/Desktop with Linux containers for Testcontainers and Compose.

Use the pinned SDK to build the `net10.0` projects and `.slnx` solution consistently
in Visual Studio, CLI builds, CI, and Docker builds.

## Local runtime

Use the [repository quick-start](../README.md) for containerized development.
The development stack binds exposed services to loopback, uses development-only
credentials, and must not be exposed to the public Internet.

For native backend debugging, configure the database, NATS endpoint, encryption
key, and administration authentication before running the API/Worker projects.
Prefer environment variables or user secrets, never committed production values.
See [authentication](self-hosting/admin-authentication.md) and
[configuration](self-hosting/production-configuration.md).

## Backend validation

```powershell
dotnet restore NuntivusServer.slnx
dotnet build NuntivusServer.slnx --no-restore
dotnet test NuntivusServer.UnitTests --no-restore
dotnet test NuntivusServer.IntegrationTests --no-restore
```

Run builds and backend tests serially. Integration tests create disposable Docker
containers and can take longer than unit tests. Never point integration fixtures
at a production database.

For portable CLI/CI builds, build the API, Worker, and test `.csproj` files
explicitly and validate the dashboard through npm. The Visual Studio dashboard
`.esproj` is an IDE integration, not a substitute for frontend validation.

## Dashboard validation

```powershell
cd NuntivusServer.Dashboard
npm ci
npm run build
npm run lint
npm test
npx playwright install chromium
npm run dev -- --host 127.0.0.1
```

In another terminal in that directory:

```powershell
npm run test:e2e
```

`PLAYWRIGHT_BASE_URL` overrides the default `http://localhost:5173` browser-test
server. These browser journeys mock API requests: they do not certify live
provider integrations or production networking.

## Dependency and security checks

Run production and full npm audits separately:

```powershell
npm audit --omit=dev
npm audit
```

NuGet lockfiles are committed. Use `dotnet restore --locked-mode` in CI and
regenerate the relevant lockfiles intentionally when changing package references.
The pinned build SDK is 10.0.401; the API and Worker target .NET 10.

The dashboard has narrowly scoped `minimatch` overrides for ESLint/config-array
and TypeScript-ESLint because their current parent ranges include the vulnerable
glob implementation. Recheck compatibility with lint/build/tests when updating
these parents, and remove the overrides once their supported ranges include a fix.

Use `dotnet list <project.csproj> package --vulnerable --include-transitive` for
backend projects; solution-wide package auditing can stop at the `.esproj`.
Treat advisory reports as time-sensitive evidence and trace transitive parents.
Do not use force-fixes, disabled lint rules, skipped tests, or increased timeouts
to hide failures.

Before publication, run the full-history secret scanner described in the
[security policy](../SECURITY.md). A deleted credential still requires revocation
or rotation; never paste findings into public issues.

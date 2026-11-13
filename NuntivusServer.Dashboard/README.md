# NuntivusServer.Dashboard

Admin dashboard frontend for NuntivusServer.

## Stack

- React 19 + TypeScript + Vite
- React Router (navigation)
- TanStack Query (server state)
- Redux Toolkit + React Redux (global UI state)
- React Context (lightweight app-scoped state)
- Radix Themes (lightweight, MIT, commercial-friendly UI library)

## Architecture

- `src/app`: app bootstrap and providers composition
- `src/routes`: route map
- `src/layouts`: shell/layout components
- `src/pages`: page-level components
- `src/services/api`: direct calls to `NuntivusServer.Api`
- `src/features`: query-based feature hooks
- `src/store`: Redux slices/store
- `src/context`: cross-cutting app context

State ownership:

- TanStack Query: backend/server data
- Redux: global UI controls (example: sidebar visibility)
- Context: workspace scope and lightweight app context

## Environment

Optional `.env.local` values:

- `VITE_API_BASE_URL` (default: `https://localhost:7123`)

Use Node.js 24 and `npm ci` to install the locked dependencies. For the development
Compose API, set `VITE_API_BASE_URL=http://localhost:8080` in `.env.local` before
starting Vite. Public production deployments should use an HTTPS API origin.
Docker deployments use the runtime settings below instead of `.env.local`.

## Docker image

Build from the repository root with Docker Desktop using Linux containers:

```powershell
docker build -f NuntivusServer.Dashboard/Dockerfile -t sasaspo/nuntivus-server-dashboard:0.1.0 .
```

Run against a local development API on port 8080:

```powershell
docker run --rm --name nuntivus-dashboard -p 127.0.0.1:5173:8080 -e NUNTIVUS_API_BASE_URL=http://localhost:8080 sasaspo/nuntivus-server-dashboard:0.1.0
```

Open `http://localhost:5173`. The image runs nginx as a non-root user on port 8080,
supports direct navigation to Dashboard routes, and exposes `/health/live` for
container health checks. This endpoint checks static serving, not API connectivity.

The same image supports different installations through startup settings:

- `NUNTIVUS_API_BASE_URL` (required): browser-reachable HTTP(S) API origin, without
  credentials, a path, query, or fragment. Do not use Docker service names such as
  `http://api:8080`; requests originate from the user's browser, not the container.
- `NUNTIVUS_APP_ENV` (optional): environment label, default `production`.
- `NUNTIVUS_FEATURE_FLAGS` (optional): comma-separated public feature flags.

Settings are generated at startup, loaded before the app, and served without
caching. Recreate the container to change its environment. These settings are public;
never include tokens, passwords, or other secrets. No API address is baked into the
image. Local API origins are supported only when the Dashboard is also opened from
a loopback browser origin.

For public deployments, use a trusted HTTPS reverse proxy serving the Dashboard
and API under the same origin, and set `NUNTIVUS_API_BASE_URL` to that origin.
The API currently enables loopback CORS only in Development; a separate production
Dashboard origin requires deliberate CORS configuration, which this image does not
provide. Do not mix an HTTPS Dashboard with an HTTP API.

After local validation and Docker Hub login, publish manually:

```powershell
docker push sasaspo/nuntivus-server-dashboard:0.1.0
```

Use a new version tag for subsequent releases. Building or running the image does
not publish it. The container remains subject to the project's early-development,
not-production-ready status.

## Scripts

- `npm run dev` - start local dev server
- `npm run lint` - run eslint
- `npm run build` - type-check and build
- `npm test` - run non-browser regression tests
- `npm run test:e2e` - run browser journeys against a running Vite server
- `npm run preview` - preview production build

## Authentication and coverage

The dashboard manages Sources, Destinations, customers, contracts, event schemas,
deliveries, SLOs, alerts, and permission-gated exports. The customer portal uses a
separate read-only session. There is no built-in default admin password.

See [admin authentication](../docs/self-hosting/admin-authentication.md) for account
setup and [development validation](../docs/development.md) for browser installation,
test server configuration, and audit commands. Browser tests mock API boundaries;
they are not production integration certification.

## License

[Apache License 2.0](../LICENSE). Dependencies retain their own licenses.

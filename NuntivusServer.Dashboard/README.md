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
starting Vite. Production builds require a non-localhost HTTPS API URL.

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

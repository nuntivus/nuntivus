# KairoServer.Dashboard

Admin dashboard frontend for KairoServer.

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
- `src/services/api`: direct calls to `KairoServer.Api`
- `src/features`: query-based feature hooks
- `src/store`: Redux slices/store
- `src/context`: cross-cutting app context

State ownership:

- TanStack Query: backend/server data
- Redux: global UI controls (example: sidebar visibility)
- Context: workspace scope and lightweight app context

## Environment

Optional `.env.local` values:

- `VITE_API_BASE_URL` (default: `https://localhost:5001`)

## Scripts

- `npm run dev` - start local dev server
- `npm run lint` - run eslint
- `npm run build` - type-check and build
- `npm run preview` - preview production build

## Current MVP baseline

- Admin shell with routes (`/`, `/deliveries`, `/settings`)
- Direct API probe to `/health/ready`
- Provider composition for Router + Query + Redux + Context + Radix Theme

# Frontend Architecture

> High-level architecture reference. For implementation patterns see [`docs/architectural_patterns.md`](architectural_patterns.md).

## Stack

| Concern | Choice |
|---|---|
| Framework | Angular 21 — standalone components |
| Rendering | Angular SSR + hydration |
| PWA | Service worker + web manifest (`ngsw-config.json`) |
| UI | PrimeNG 21 + Tailwind CSS 4 |
| State | NgRx 21 — root store + feature providers |
| Data / Auth | HttpClient with interceptors (JWT bearer + 401 refresh) |
| Package manager | Bun 1.3+ |

## Folder Structure

```
src/frontend/src/app/
├── app.config.ts          # Global providers: router, HTTP interceptors, NgRx, PrimeNG, hydration, SW
├── app.routes.ts          # Public auth routes + guarded shell routes
├── core/
│   ├── auth/              # Auth service, storage, endpoint constants, session models
│   ├── guards/            # authGuard (protects shell routes)
│   ├── interceptors/      # authInterceptor (bearer token + 401 refresh), HTTP loading tracker
│   ├── layout/            # App shell and navigation components
│   └── state/             # Root NgRx reducers/actions (appShell, httpUi)
└── features/
    ├── auth/              # Login/register pages + NgRx registration state
    ├── overview/          # Overview feature page + local feature state
    ├── operations/        # Operations feature page + local feature state
    └── shops/             # Create-shop overlay + shop service
```

## Routing and Auth Flow

1. Public routes: `/login`, `/register`
2. Shell and all app routes are protected by `authGuard`
3. On bootstrap, `APP_INITIALIZER` restores auth session from storage
4. `authInterceptor` attaches bearer token; on 401 attempts one silent refresh (loop prevention included)
5. Failed refresh clears session and redirects to `/login`

## State Architecture

- **Root store** (always loaded):
  - `appShell` — layout / sidebar UI state
  - `httpUi` — pending request counter (drives global loading indicator)
  - `authRegistration` — register form submit/error state
- **Feature state** — provided per route for `overview` and `operations` (lazy)
- Component-local state for simple UI-only cases; NgRx for anything shared or async

## Key Patterns

- **Standalone components** — no NgModules; all imports declared per component
- **Feature-first layout** — each feature owns its routes, state, and components under `features/<name>/`
- **Interceptor chain** — auth token injection and loading tracking are cross-cutting; wired in `app.config.ts`
- **i18n** — language list registered in `register-page.component.ts`; native language names keyed by locale (`en-IN`, `hi-IN`, `ta-IN`, etc.)

## Test Structure

| Suite | Path |
|---|---|
| Unit specs | `src/frontend/src/**/*.spec.ts` (Vitest + Angular test utilities) |
| Unit (workspace) | `tests/frontend/unit/` |
| E2E | `tests/frontend/e2e/` |
| Coverage output | `src/frontend/coverage/` |

## Workspace Files

- `src/frontend/angular.json` — project build/test targets
- `src/frontend/package.json` — scripts (`start`, `build`, `test`, `serve:ssr:INVENTORY`)
- `src/frontend/ngsw-config.json` — PWA service worker config

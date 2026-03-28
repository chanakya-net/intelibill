# Frontend Architecture Snapshot

Purpose: quick architecture context for humans and AI.

## Stack and Core Patterns
- Framework: Angular 21 with standalone components.
- Rendering/build: Angular SSR + hydration, production PWA service worker.
- UI: PrimeNG components + Tailwind CSS.
- State: NgRx root store (app shell, HTTP UI, auth registration) + feature state providers.
- Data/auth: HttpClient with interceptors for loading state and JWT refresh flow.
- Package manager/runtime: Bun.

## Folder Overview (Frontend)
- `src/frontend/src/app/`
  - `app.config.ts`: global providers (router, http interceptors, NgRx, PrimeNG, hydration, SW).
  - `app.routes.ts`: route map with public auth routes and guarded shell routes.
- `src/frontend/src/app/core/`
  - `auth/`: auth service, storage, endpoint constants, session models.
  - `guards/`: route guards (`authGuard`).
  - `interceptors/`: auth token/refresh + global HTTP loading tracker.
  - `layout/`: application shell and navigation.
  - `state/`: root reducers/actions for shell and HTTP UI.
- `src/frontend/src/app/features/`
  - `auth/`: login/register pages and NgRx registration effects/reducer/selectors.
  - `overview/`, `operations/`: feature pages and local feature state.
  - `shops/`: create-shop overlay and service.

## Routing and Auth Flow (High Level)
1. Public routes: `/login`, `/register`.
2. Protected app routes live under shell component and use `authGuard`.
3. On app bootstrap, `APP_INITIALIZER` calls auth session bootstrap.
4. `authInterceptor` adds bearer token and attempts refresh on 401 (with loop prevention).
5. Failed refresh clears session and redirects to login for most endpoints.

## State Management Shape
- Current status: NgRx is already integrated in this frontend.
- Root reducers:
  - `appShell` (layout UI state)
  - `httpUi` (pending request counter)
  - `authRegistration` (register submit/error state)
- Feature state provided per route for `overview` and `operations`.
- Recommendation: keep NgRx as the single shared/global state approach; use component-local state for simple UI-only cases.

## Test Locations
- Frontend unit specs (Vitest + Angular test utilities):
  - `src/frontend/src/**/*.spec.ts`
- Workspace-level test folders:
  - `tests/frontend/unit/`
  - `tests/frontend/e2e/`
- Frontend test output/artifacts (if generated):
  - `src/frontend/coverage/`

## Practical Notes
- Main frontend workspace files:
  - `src/frontend/angular.json`
  - `src/frontend/package.json`
  - `src/frontend/ngsw-config.json`

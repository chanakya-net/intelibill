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
| i18n | Transloco — HTTP-loaded JSON translation files |
| Package manager | Bun 1.3+ |

## Folder Structure

```
src/frontend/src/app/
├── app.config.ts          # Global providers: router, HTTP interceptors, NgRx, PrimeNG, Transloco, hydration, SW
├── app.routes.ts          # Public auth + OAuth callback routes + guarded shell
├── core/
│   ├── auth/              # Auth service, storage, endpoint constants, session models
│   ├── guards/            # authGuard (protects shell routes)
│   ├── i18n/              # Transloco setup, language constants, LocalizationService
│   ├── interceptors/      # authInterceptor (bearer token + 401 refresh), HTTP loading tracker
│   ├── layout/            # ShellComponent + shell.routes.ts (lazy feature state providers)
│   ├── services/          # AudioService, BarcodeDetectorService, CameraStreamService, ProductCatalogSyncService
│   ├── state/             # Root NgRx reducers/actions (appShell, httpUi)
│   └── storage/           # IndexedDB services: product catalog, sales cart, inventory draft
├── features/
│   ├── auth/              # Login, register (multi-step routes), OAuth callback
│   ├── bank-accounts/     # Bank accounts CRUD
│   ├── customers/         # Customer list + account/ledger sub-page
│   ├── expenses/          # Expense recording + list
│   ├── inventory/         # Inbound stock, batch list, batch management
│   ├── not-found/         # 404 page (wildcard route)
│   ├── sales/             # New sale (POS), sales list, profit-loss page
│   ├── shops/             # Create/switch shop overlay + shop service
│   ├── suppliers/         # Supplier list + ledger
│   └── users/             # Shop user management + profile
└── shared/
    └── components/
        └── bank-account-form/   # Reusable bank account form component
```

## Routing and Auth Flow

1. Public routes: `/login`, `/register/*`, `/auth/callback` (OAuth)
2. Shell and all app routes protected by `authGuard`
3. On bootstrap, `APP_INITIALIZER` restores auth session from storage
4. `authInterceptor` attaches bearer token; on 401 attempts one silent refresh (loop prevention included)
5. Failed refresh clears session and redirects to `/login`
6. Default authenticated route redirects to `/users`

## State Architecture

- **Root store** (always loaded):
  - `appShell` — layout / sidebar UI state
  - `httpUi` — pending request counter (drives global loading indicator)
  - `authRegistration` — register form submit/error state
- **Shell-scoped feature state** (provided lazily at the shell route, shared across child pages):
  - `shops`, `users`, `suppliers`, `inventory`, `customers`, `sales`, `expenses`, `bankAccounts`
- Component-local state for simple UI-only cases; NgRx for anything shared or async

## Key Services (core/services)

| Service | Purpose |
|---|---|
| `ProductCatalogSyncService` | Streams item catalog from backend SSE endpoint into IndexedDB |
| `BarcodeDetectorService` | Wraps browser BarcodeDetector API for scanning |
| `CameraStreamService` | Manages camera access for barcode/inventory use |
| `AudioService` | Plays feedback sounds (e.g., scan beep) |

## IndexedDB Storage (core/storage)

| Service | Store | Purpose |
|---|---|---|
| `ProductCatalogIndexedDbService` | `product-catalog` | Offline item catalog synced from backend stream |
| `SalesCartIndexedDbService` | `sales-cart` | Persists in-progress POS cart across page reloads |
| `InventoryDraftIndexedDbService` | `inventory-draft` | Persists in-progress inbound stock entry |

## Key Patterns

- **Standalone components** — no NgModules; all imports declared per component
- **Feature-first layout** — each feature owns its routes, state, and components under `features/<name>/`
- **Interceptor chain** — auth token injection and loading tracking are cross-cutting; wired in `app.config.ts`
- **i18n via Transloco** — translation files loaded over HTTP; language constants in `core/i18n/language.constants.ts`
- **Offline-first POS** — product catalog, sales cart, and inventory drafts persisted in IndexedDB for offline resilience

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

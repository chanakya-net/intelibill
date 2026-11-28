# Merchant Web (`INVENTORY`)

The merchant web app of intelibill: point of sale, inventory, purchasing, customers and suppliers, tills, billing and offline selling for shop owners and staff. It is a client-rendered Angular PWA that talks to the merchant API ([`src/backend/intelibill`](../../backend/intelibill/README.md)).

## Stack

Angular 21 standalone components, NgRx 21, PrimeNG 21, Tailwind CSS 4, Transloco (9 locales in `public/assets/i18n/`), Angular service worker wrapped by a protected API worker, IndexedDB for offline data, SignalR client, Vitest, Playwright. Shared workspace packages `@intelibill/ui` and `@intelibill/api` come from [`src/frontend/shared`](../shared/README.md).

**Bun `1.3.11` only** — never `npm` or `yarn`. The lockfile is the single `bun.lock` at the repository root, which also defines the workspaces.

## Layout

| Path | Purpose |
|---|---|
| `src/app/core/` | Auth, guards, interceptors, shell layout, i18n, root state, IndexedDB storage, service-worker services |
| `src/app/features/` | One folder per feature area with its own routes, state and pages |
| `src/app/shared/` | Reusable components, forms, pipes, GST and quantity helpers, shared styles |
| `src/server.ts` | Express server: static files, deep links, `/api` and `/hubs` proxy |
| `service-worker/` | `protected-api-worker.js`, the wrapper around Angular's worker |
| `scripts/` | Service-worker finalisation, style and UI audits, test runner |
| `tests/ui-audit/` | Playwright suites (mocked, print, release, live, onboarding) |
| `public/` | Web manifest, icons, i18n JSON |
| `architecture_doc/` | Architecture and pattern docs for this project |

## Commands

Run from `src/frontend/intelibill`.

```bash
bun install
bun run start                 # dev server, http://localhost:4200
bun run build                 # ng build + wrapped service worker + server bundle
bun run serve                 # run the built server (dist/INVENTORY/server/server.mjs)
bun run test --watch=false    # all unit specs (Vitest)
bun run test --run <path-fragment>
```

## Serving and PWA

There is no server-side rendering. `bun run build` emits:

- `dist/INVENTORY/browser` — the application, served as static files.
- `dist/INVENTORY/server/server.mjs` — a small Express server that serves those files, answers deep links with `index.html`, and proxies `/api` and `/hubs` (including WebSocket upgrades) to `API_ORIGIN`. It listens on `PORT` (default `4000`).

The proxy puts the API on the origin that served the page, so there are no cross-origin requests and no CORS configuration.

Never ship Angular's bare `ngsw-worker.js`. `bun run build` wraps it with `service-worker/protected-api-worker.js` so that `/api` and `/hubs` always bypass the cache.

## Checks required when you change this app

```bash
# Templates, copy or public/assets/i18n/*.json changed: validates all 9 locales
bun run test --run src/app/core/i18n

# Duplicate style ceiling (only goes down)
bun run audit:styles:duplicates

# Service worker and release audit
bun run build && bun run audit:ui:release
```

## Documentation

- [Architecture](architecture_doc/architecture.md): folder structure, routing and auth, state, storage, shop context transitions, protected API cache isolation, offline pack and printing, shared styles, feature notes, tests.
- [Patterns](architecture_doc/patterns.md): standalone components, feature-first layout, NgRx boundaries, interceptors, guards.
- Rollout and operations notes that span projects are in the repo-root [`docs/`](../../../docs), for example [`docs/operations/offline-sales.md`](../../../docs/operations/offline-sales.md).
- Role-correct licence notices (`src/app/features/billing/status/license-notice.component.ts`) show operational contact-owner guidance in the shop shell banner and commercial renewal/suspension guidance on authorized billing account pages across all 9 locales, using server-relative bounded timers for multi-day deadlines.

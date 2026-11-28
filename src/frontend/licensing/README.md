# Admin Web (Provider Console)

Angular single-page app used by InteliBill provider staff to operate the licensing service: account
access status, restrictions, manual payments and refunds, billing documents, the plan catalogue,
administrator access, device sessions and the identity audit log. The browser title is
"InteliBill Provider Console".

- Angular project name: `ADMIN` (`angular.json`), component prefix `adm`
- Workspace package name: `intelibill-admin` (member of the root Bun workspace, single root `bun.lock`)
- Talks only to the licensing API under `/api/admin/**`

## Stack

| Concern | Choice |
|---|---|
| Framework | Angular 21 standalone components, signals, `OnPush` |
| UI | PrimeNG 21 (`@primeuix/themes`), PrimeIcons, Tailwind CSS 4 via `@tailwindcss/postcss` |
| Shared code | `@intelibill/ui`, `@intelibill/api` (workspace packages, resolved through `tsconfig.json` paths) |
| HTTP / async | Angular `HttpClient` with one functional interceptor, RxJS 7.8 |
| Build | `@angular/build:application` (browser build only) |
| Unit tests | `@angular/build:unit-test` with Vitest 4 and jsdom |
| Release tests | Playwright 1.62 (Chromium) |
| Runtime | `server.mjs` on Node 24 built-ins (`engines.node`: `>=24.0.0 <26`) |
| Package manager | Bun 1.3.11 (`cli.packageManager` is `bun`; never `npm` or `yarn`) |

There is no NgRx store, no Transloco/i18n layer and no service worker in this app.

## Layout

```text
src/frontend/licensing/
├── angular.json            # single project ADMIN, output dist/ADMIN
├── package.json            # scripts and dependencies
├── tsconfig*.json          # strict TS; path aliases to ../shared/ui and ../shared/api
├── .postcssrc.json         # Tailwind PostCSS plugin
├── server.mjs              # production runtime: static files + /api/admin proxy
├── Dockerfile              # two-stage image (Bun build, Node runtime)
├── src/
│   ├── index.html, main.ts, styles.css
│   └── app/
│       ├── app.ts, app.config.ts, app.css   # shell, providers, routes
│       ├── landing-page.component.*         # capability-driven overview
│       ├── core/auth/                       # session interceptor, cross-tab refresh coordinator
│       └── features/                        # accounts, admin-users, audit, auth, catalogue,
│                                            # coverage-exceptions, documents, payments, refunds,
│                                            # restrictions, security
└── tests/
    ├── server.test.mjs     # node:test suite for server.mjs
    └── release/            # Playwright suite against the production build
```

## Scripts

Run from `src/frontend/licensing` after `bun install` at the repository root.

| Script | Command | What it does |
|---|---|---|
| `bun run start` | `NODE_NO_WARNINGS=1 ng serve` | Dev server, `development` configuration (source maps, no optimisation). `angular.json` defines no proxy configuration. |
| `bun run build` | `ng build` | Production build (default configuration) into `dist/ADMIN/browser`, hashed output, bundle budgets enforced. |
| `bun run test` | `ng test` | Vitest unit tests (`src/**/*.spec.ts`). Use `bun run test --watch=false` for a single run. |
| `bun run audit:release` | `ng build && bun x playwright test --config tests/release/playwright.release.config.ts` | Builds, then runs the Playwright release suite against the built files. |

The runtime server has its own test suite, run from the repository root:

```bash
node --test src/frontend/licensing/tests/server.test.mjs
```

## How it is served

`server.mjs` is the production runtime and uses Node built-ins only, so the image ships no
`node_modules`. It serves the built files, answers client-side routes with `index.html`, and forwards
`/api/admin` and its descendants to the licensing API. Every other path under `/api` or `/hubs`
answers `404`.

| Variable | Default | Meaning |
|---|---|---|
| `PORT` | `4000` | Listening port |
| `API_ORIGIN` | none (required) | Licensing API origin; must be a bare `http(s)` origin without credentials, path or query |
| `ADMIN_WEB_ROOT` | `./browser` next to `server.mjs` | Directory of built files |

`Dockerfile` (build context is the repository root) installs the root Bun workspace with
`bun install --frozen-lockfile`, runs `bun run build` here, then copies `dist/ADMIN/browser` and
`server.mjs` into a Node 24 runtime stage that removes npm, exposes `4000`, and runs
`node server.mjs` as user `node`.

In the root [`docker-compose.yml`](../../../docker-compose.yml) the app is the `admin-frontend`
service (profile `full`). It is published on `127.0.0.1:${INTELIBILL_ADMIN_WEB_PORT:-4001}` mapped to
container port `4000`, sets `API_ORIGIN=http://licensing-api:8080`, and starts once `licensing-api`
is healthy.

## Tests

- **Unit**: `*.spec.ts` beside the code under `src/app`, run by `bun run test`.
- **Runtime server**: `tests/server.test.mjs` (`node:test`), covering static serving, path
  confinement, the `/api/admin`-only proxy and `API_ORIGIN` validation.
- **Release**: `tests/release/*.spec.ts`, Playwright in Chromium against `dist/ADMIN/browser`, served
  by `tests/release/static-server.ts` with API responses stubbed per test. Output goes to
  `test-results/release`.

## Notification preview localization (P07-01)

The read-only scheduled notification preview panel (`src/app/features/notifications/`, mounted inside Accounts status) uses the merchant-pinned `@ngneat/transloco` package with `AdminTranslocoLoader` and nine locale JSON assets (`src/assets/i18n/{en,hi,ta,te,bn,ml,kn,mr,gu}-IN.json`). Wider admin localization remains tracked under `#931`.

## Documentation

- [Architecture of this app](architecture_doc/architecture.md)
- [Shared web packages (`@intelibill/ui`, `@intelibill/api`)](../shared/architecture_doc/architecture.md)
- [Licensing backend](../../backend/licensing/architecture_doc/architecture.md)
- [Merchant web app](../intelibill/architecture_doc/architecture.md)
- [Licensing stack rollout and compose notes](../../../docs/licensing-rollout.md)

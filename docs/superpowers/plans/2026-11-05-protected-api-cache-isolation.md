# Protected API Cache Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** No service-worker or browser cache can return one identity's protected API response to another identity, including tabs that still run a previously deployed build.

**Architecture:** Protected traffic (`/api/**`, `/hubs/**`) never reaches Angular's worker logic. The page registers a small wrapper worker (`app-worker.js`). Its `fetch` listener runs before Angular's and hands protected requests straight to the network. Its `activate` handler empties the caches the old `api-fresh` rule filled. The page also marks its own API calls with `ngsw-bypass`, removes every `/api` rule from `ngsw-config.json`, and runs a page-side migration that purges legacy caches, hurries the wrapper into control, and reports `safe | reloadRequired | blocked`. The API answers `Cache-Control: no-store` on every protected response.

**Tech Stack:** Angular 21.2 (`@angular/service-worker` 21.2.8), Bun, Vitest, Playwright 1.62; ASP.NET Core 10, xUnit.

**Spec:** Backlog item 23, "Protected API cache isolation" (pasted plan; evidence `docs/reviews/2026-10-21-market-readiness-evidence/service-worker-cache-result.json` and `service-worker-cache-probe.mjs`).

## Analysis (source at `fbff57b09`, 2026-11-05)

| Finding | Evidence | Consequence |
|---|---|---|
| `api-fresh` caches `/api/**` for 1 h with an 8 s timeout, and Angular compiles data groups with `ignoreVary: true` | `src/frontend/ngsw-config.json:55`, built `ngsw.json` | The cache key is the URL only, so a slow ShopB call gets ShopA's body. Reproduced by the review probe. |
| Data-group caches are named `ngsw:<scope>:<v>:data:<group>:cache` with tables `ngsw:<scope>:db:<v>:data:<group>:{age,lru}`, keyed by the group's `version`, not the app hash | `ngsw-worker.js:392,830-832,1142`, `NamedCacheStorage` prefix `ngsw:${scopePath}` (line 64) | The migration targets these exact names. No other names are invented. |
| Each client stays on the app version it loaded with. That version's data groups keep serving it until the tab reloads, and `cleanupCaches()` keeps a version's caches while any client uses it | `ngsw-worker.js:2083-2100` | **Removing the config rule alone does not protect an open old tab (RF3).** Old code sends no bypass header. The only way to protect that tab is a new worker script, which Angular activates with `skipWaiting` + `clients.claim` (lines 1553-1558) and which can intercept requests before Angular does. |
| `ngsw-bypass` header or query is honoured before any group matching | `ngsw-worker.js:1589` | Previously deployed workers honour it too. It is safe as a permanent page-side guard. |
| `product-catalog-sync.service.ts:87` streams `items/stream` with raw `fetch`, no bypass | source | A second leak path, outside the HttpClient interceptor. |
| The interceptor attaches the bearer token to every request, including `/assets/i18n/*.json` and any cross-origin URL | `auth.interceptor.ts:38-40` | The spec requires that arbitrary resources get no token. |
| Navigation fallback matches `/api/...` paths (default `navigationUrls`) | built `ngsw.json` | A document navigation to an API URL would be answered with `index.html`. |
| `server.ts` serves every non-index file, including `ngsw-worker.js`/`ngsw.json`, with `max-age=1y` | `src/frontend/src/server.ts:51-61` | A wrapper that `importScripts` the Angular worker would pick up a year-old copy. Serve worker files `no-cache` and register with `updateViaCache: 'none'`. |
| The API sets `no-store` on three actions only | `ItemsController.cs:60`, `SalesController.cs:73`, `ConnectivityController.cs:10` | No defence in depth for the browser HTTP cache. |
| Live UI config blocks service workers. No plan-48 release harness exists | `tests/ui-audit/live/playwright.live.config.ts:30` | This plan adds a focused release harness against the built output. |

### Deviations from the pasted plan

1. **Wrapper worker added.** The pasted plan protects only pages that run the new code. An old tab keeps its old version's `api-fresh` rule until it reloads. Only a new worker script, which claims every client, closes RF3.
2. **No request gate.** The pasted plan blocks online authenticated requests until a safe worker controls the page. Here, every same-origin API call carries `ngsw-bypass`, and every deployed Angular worker honours that header, so no authenticated request ever reaches an old worker. A gate would add no isolation and would stop billing on an unrelated storage error. `reloadRequired` and `blocked` drive a non-blocking recovery notice instead.
3. **Bypass is permanent, not transitional.** It is cheap. It is limited to same-origin API calls, because a custom header on cross-origin requests (development) would need a CORS preflight.
4. **App-level DOM assertions are out of scope.** No production app harness exists yet (plan 48). The release harness checks response bodies and on-page rendering through the real built worker. Interceptor behaviour is pinned by unit tests.

## Global Constraints

- Angular web and .NET API only; mobile excluded. Frontend uses Bun only; C# warnings are errors; CPM for packages.
- Never delete all CacheStorage or any IndexedDB database. Purge only the three legacy protected data groups: `api-fresh`, `ping-no-cache`, `offline-sales-api-no-cache`.
- Never re-add generic authenticated GET caching. A future per-feature cache needs its own identity/tenant design.
- Exception messages, tokens and URLs are never logged by hand (existing logging rule).
- New UI copy goes into all nine locale files under `src/frontend/public/assets/i18n`.

## Review Focus

- Same-origin non-API requests (`/assets/i18n/en-IN.json`) get neither a bearer token nor a bypass header. Test in Task 3.
- A document navigation to `/api/...` is not answered with the app shell. Test in Task 2 (generator, `navigationUrls`).
- CacheStorage throws during a purge: state is `blocked`, nothing else is deleted, and a later run retries. Test in Task 4.
- The legacy worker never hands over (offline at upgrade, missing `app-worker.js`): state is `reloadRequired` after the timeout, and requests keep flowing. Test in Task 4.
- A purge never touches app asset caches (`ngsw:/:<hash>:assets:*`), other origins' caches or IndexedDB. Tests in Task 2 (worker), Task 4 (page) and Task 5 (real browser, offline queue).

---

### Task 1: API answers `Cache-Control: no-store` on protected paths

**Files:**
- Create: `src/backend/Intelibill.Api/Middleware/ProtectedResponseCacheMiddleware.cs`
- Modify: `src/backend/Intelibill.Api/Program.cs` (register right after `UseForwardedHeaders()`, before `ExceptionHandlingMiddleware`)
- Test: `tests/backend/unit/Intelibill.Api.Unit.Tests/Middleware/ProtectedResponseCacheMiddlewareTests.cs`, `tests/backend/integration/Intelibill.Integration.Tests/ProtectedResponseCacheIntegrationTests.cs`

**Interfaces:**
- Produces: `public sealed class ProtectedResponseCacheMiddleware(RequestDelegate next) { Task InvokeAsync(HttpContext context); }`. Protected = `Request.Path.StartsWithSegments("/api")` or `("/hubs")`. Sets `Cache-Control: no-store` before `next` and again from `Response.OnStarting`, so a header written by an endpoint or by the exception middleware is overwritten.

- [ ] **Step 1: Write unit tests.** Use a response feature that records `OnStarting` callbacks and fires them. Cases: `ApiOk_IsNoStore` (200), `ApiUnauthorizedAndForbidden_AreNoStore` (401, 403), `EndpointCachePolicy_IsOverridden` (endpoint sets `public, max-age=60` → `no-store`), `UnhandledException_ProblemIsNoStore` (composed with the real `ExceptionHandlingMiddleware` throwing → 500 `application/problem+json` + `no-store`), `HubPath_IsNoStore`, `NonProtectedPath_KeepsItsPolicy` (`/health/live` and `/favicon.ico` keep `public, max-age=3600` / no header), `LookalikePath_IsNotProtected` (`/apis/x`, `/api-docs`).
- [ ] **Step 2: Run** `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter FullyQualifiedName~ProtectedResponseCacheMiddlewareTests`. Expect a compile failure (type missing).
- [ ] **Step 3: Implement the middleware and register it in `Program.cs`.**
- [ ] **Step 4: Write the integration test:** `/api/shops/me` without a token → 401 + `no-store`; with an enrolled user's token → 200 + `no-store`; `/health/live` → no `no-store`.
- [ ] **Step 5: Run** the unit filter (PASS), then `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~ProtectedResponseCacheIntegrationTests` (PASS, needs Docker).
- [ ] **Step 6: Commit** `feat(api): answer no-store on every protected response`.

### Task 2: Worker never handles protected traffic

**Files:**
- Modify: `src/frontend/ngsw-config.json` (drop `dataGroups`; add `"!/app-worker.js"` to the `app` group; add `navigationUrls` = Angular defaults + `"!/api/**"`, `"!/hubs/**"`)
- Create: `src/frontend/public/app-worker.js`
- Create: `src/frontend/src/app/core/service-worker/service-worker-registration.ts`, `src/frontend/src/app/core/service-worker/protected-cache-names.ts`
- Modify: `src/frontend/src/app/app.config.ts` (`provideServiceWorker(APP_WORKER_SCRIPT, { enabled, registrationStrategy, ...APP_WORKER_REGISTRATION })`)
- Create: `src/frontend/src/server-cache-policy.ts`; modify `src/frontend/src/server.ts` to use it
- Test: `src/frontend/scripts/tests/ngsw-config.spec.ts`, `src/frontend/scripts/tests/app-worker.spec.ts`, `src/frontend/scripts/tests/server-cache-policy.spec.ts`

**Interfaces:**
- Produces (`service-worker-registration.ts`): `APP_WORKER_SCRIPT = 'app-worker.js'`, `APP_WORKER_REGISTRATION = { updateViaCache: 'none' } as const`, `isAppWorkerScript(scriptUrl: string): boolean` (path ends with `/app-worker.js`).
- Produces (`protected-cache-names.ts`): `LEGACY_PROTECTED_DATA_GROUPS = ['api-fresh', 'ping-no-cache', 'offline-sales-api-no-cache'] as const`, `isLegacyProtectedCacheName(name: string): boolean`, matching `^ngsw:[^:]*:(?:db:)?\d+:data:(<groups>):(?:cache|age|lru)$`.
- Produces (`server-cache-policy.ts`): `cacheControlFor(relativePath: string): string | null`, which returns `'no-cache'` for `index.html`, `ngsw.json`, `ngsw-worker.js`, `app-worker.js`, `safety-worker.js`, `worker-basic.min.js`, `manifest.webmanifest` and `null` otherwise (the static `maxAge` applies).
- `app-worker.js`: (1) `fetch` listener registered **before** `importScripts('./ngsw-worker.js')`. For a same-origin request whose path starts with `/api/` or `/hubs/`, it calls `event.stopImmediatePropagation()` and does not call `respondWith`, so the browser fetches from the network. (2) `activate` listener: for every cache whose name matches the same pattern as `isLegacyProtectedCacheName`, delete each entry, then delete the cache. Emptying first matters because a live old worker may still hold a handle.

- [ ] **Step 1: Write `ngsw-config.spec.ts`.** Run Angular's `Generator` (`@angular/service-worker/config`) over the real `ngsw-config.json`, using an in-memory `Filesystem` (`/index.html`, `/index.csr.html`, `/main-ABC.js`, `/styles-DEF.css`, `/app-worker.js`, `/assets/i18n/en-IN.json`, `/fonts/inter.woff2`, `/favicon.ico`, `/manifest.webmanifest`). Test `protected_api_not_in_generated_data_groups`: no data group pattern matches `/api/customers`, `/api/sales`, `/api/bank-accounts`, `/api/x/y?z=1`; no asset group URL or pattern covers `/api/`. Test that the shell assets are listed: index, js, css, i18n json, woff2. Test that `/app-worker.js` is not listed. Test that navigation regexes accept `/sales/new` but reject `/api/customers` and `/hubs/products`.
- [ ] **Step 2: Write `app-worker.spec.ts`.** Load `public/app-worker.js` into a `node:vm` sandbox (fake `self`, `importScripts` recorder, fake `caches`, `location.origin = https://shop.test`). Test that the fetch listener is registered before `importScripts` is called. Test that same-origin `/api/customers`, `/api/ping?ngsw-bypass=true` and `/hubs/products/negotiate` stop propagation without `respondWith`. Test that `/main.js`, `/assets/i18n/en-IN.json`, `/` and `https://cdn.test/api/x` are untouched. Test that `activate` empties and deletes exactly the names `isLegacyProtectedCacheName` accepts, and keeps `ngsw:/:abc123:assets:app:cache`, `ngsw:/:db:control` and `other-cache`.
- [ ] **Step 3: Write `server-cache-policy.spec.ts`** covering the list above, plus `chunk-ABC.js` → `null`.
- [ ] **Step 4: Run** `cd src/frontend && bun test scripts/tests/ngsw-config.spec.ts scripts/tests/app-worker.spec.ts scripts/tests/server-cache-policy.spec.ts`. Expect FAIL.
- [ ] **Step 5: Implement** the config, the worker, the modules, and the wiring in `app.config.ts` and `server.ts`.
- [ ] **Step 6: Run** the same command (PASS), then `bun run build`. Confirm `dist/INVENTORY/browser/app-worker.js` exists and the built `ngsw.json` has no `dataGroups`.
- [ ] **Step 7: Commit** `fix(web): keep protected API traffic out of the service worker`.

### Task 3: Page requests carry the right credentials and bypass

**Files:**
- Create: `src/frontend/src/app/core/service-worker/service-worker-bypass.ts` (+ `.spec.ts`)
- Modify: `src/frontend/src/app/core/interceptors/auth.interceptor.ts` (+ spec), `src/frontend/src/app/core/services/product-catalog-sync.service.ts` (+ spec), `network-status.service.ts`, `features/sales/services/offline-sales-snapshot-sync.service.ts` (switch their private bypass helpers to the shared one)

**Interfaces:**
- Produces: `SERVICE_WORKER_BYPASS_HEADER = 'ngsw-bypass'`; `withServiceWorkerBypassQuery(url: string): string`; `classifyApiRequest(url: string, apiBaseUrl: string, pageUrl: string): { isApiRequest: boolean; bypassServiceWorker: boolean }`. `isApiRequest` means the resolved URL is under the resolved `apiBaseUrl` (same origin, path equal to the base or under `base + '/'`). `bypassServiceWorker` means `isApiRequest` and the same origin as `pageUrl`. A malformed URL gives both false.
- Interceptor: the owner-scope check stays first. A non-API request passes through `next(request)` untouched (no token, no refresh, no scope filtering). An API request gets `Authorization` when a token exists, plus `ngsw-bypass: true` when `bypassServiceWorker`. The retry after a refresh keeps both headers.

- [ ] **Step 1: Write `service-worker-bypass.spec.ts`.** Prod-like inputs: base `/api`, page `https://shop.test/sales` → `/api/customers` gives `{true, true}`. `/assets/i18n/en-IN.json` gives `{false, false}`. `https://cdn.test/api/x` gives `{false, false}`. `/apis/x` gives `{false, false}`. Dev-like inputs: base `http://localhost:5277/api`, page `http://localhost:4200/` → `http://localhost:5277/api/sales` gives `{true, false}`. `withServiceWorkerBypassQuery('/api/ping')` → `/api/ping?ngsw-bypass=true`; with an existing `?a=1` → `&ngsw-bypass=true`.
- [ ] **Step 2: Update the interceptor spec.** Existing relative `/api/...` URLs become `${API_BASE_URL}/...`. New tests: `does_not_send_token_to_non_api_resources` (`assets/i18n/hi-IN.json`, `https://cdn.example.com/a.png` → no `Authorization`, `next` gets the original request); `cross_origin_api_gets_no_bypass_header` (test env base is cross-origin → no `ngsw-bypass`); `non_api_401_does_not_refresh`.
- [ ] **Step 3: Update `product-catalog-sync.service.spec.ts`.** The stream `fetch` URL carries `ngsw-bypass=true` and the init has `cache: 'no-store'`.
- [ ] **Step 4: Run** `bun run test --run src/app/core/service-worker`, `--run src/app/core/interceptors`, `--run src/app/core/services/product-catalog-sync`. Expect FAIL.
- [ ] **Step 5: Implement**, then run the same commands. Expect PASS.
- [ ] **Step 6: Commit** `fix(web): scope bearer tokens to the API and bypass the worker for it`.

### Task 4: Page-side migration and recovery notice

**Files:**
- Create: `src/frontend/src/app/core/service-worker/protected-cache-migration.service.ts` (+ `.spec.ts`), `protected-cache-safety.service.ts` (+ `.spec.ts`)
- Modify: `src/frontend/src/app/core/services/app-reload.service.ts` (add `reloadPage(): void`), `src/frontend/src/app/app.config.ts` (start safety in `initializeAppServices` without awaiting it), `src/frontend/src/app/core/layout/shell.component.{ts,html,scss,spec.ts}`, nine locale files

**Interfaces:**
- Consumes: Task 2's `APP_WORKER_SCRIPT`, `APP_WORKER_REGISTRATION`, `isAppWorkerScript`, `isLegacyProtectedCacheName`.
- Produces:
  - `type CacheSafetyState = 'safe' | 'reloadRequired' | 'blocked'`.
  - `PROTECTED_CACHE_ENVIRONMENT: InjectionToken<{ caches: CacheStorage | null; serviceWorker: ServiceWorkerContainer | null }>` (factory reads the globals; tests pass fakes).
  - `ProtectedCacheMigrationService.purgeLegacyCaches(): Promise<boolean>`: true when no legacy-named cache holds an entry afterwards. It empties and deletes matching caches only, and returns false on any CacheStorage error.
  - `ProtectedCacheMigrationService.ensureSafeVersion(): Promise<CacheSafetyState>`. No `caches` → `safe`. Purge fails → `blocked`. Controller is null or the app worker → `safe`. Controller is legacy → `serviceWorker.register(APP_WORKER_SCRIPT, APP_WORKER_REGISTRATION)`, then wait for `controllerchange` to an app worker for up to `CONTROLLER_HANDOVER_TIMEOUT_MS = 15_000`, then purge again → `safe`. Purge fails → `blocked`. Timeout or register rejects → `reloadRequired`.
  - `ProtectedCacheSafetyService`: `state: Signal<CacheSafetyState | 'checking'>`; `start(): void` (idempotent; runs `ensureSafeVersion` and registers `ShopContextService.onDetach` to re-purge on every owner change, returning the purge promise); `reload(): void` → `AppReloadService.reloadPage()`.
  - i18n keys: `shell.cacheSafety.reloadRequired` = "Intelibill finished updating in the background. Reload this page to complete the update.", `shell.cacheSafety.blocked` = "Saved data from an earlier version could not be removed. Reload this page to try again.", `shell.cacheSafety.reload` = "Reload".

- [ ] **Step 1: Write the migration spec** with fake `CacheStorage`/`Cache` and a fake `ServiceWorkerContainer` (an `EventTarget` with a settable `controller`). Cases: `no_cache_storage_is_safe`; `purges_only_legacy_entries` (legacy entries emptied and deleted; asset/db-control/other caches untouched; `indexedDB.deleteDatabase` never called); `cleanup_preserves_unsynced_invoice` (the purge never opens or deletes the `intelibill-offline-sales-queue` database; verified with an `indexedDB` spy); `uncontrolled_or_app_worker_is_safe`; `legacy_controller_hands_over` (register called with `('app-worker.js', { updateViaCache: 'none' })`, `controllerchange` → `safe`, purge ran twice); `legacy_controller_timeout_is_reload_required` (fake timers); `register_rejects_is_reload_required`; `keys_throws_is_blocked`; `delete_throws_midway_is_blocked_then_retry_succeeds` (interrupted cleanup).
- [ ] **Step 2: Write the safety spec.** `start` sets `checking` and then the result. `start` twice runs once. An owner change runs a purge and returns its promise. `reload` calls `reloadPage`.
- [ ] **Step 3: Write the shell spec cases.** The notice renders with `role="alert"` for `reloadRequired` and `blocked`. It is absent for `safe` and `checking`. The button calls `reload()`.
- [ ] **Step 4: Run** `bun run test --run src/app/core/service-worker` and `--run src/app/core/layout`. Expect FAIL.
- [ ] **Step 5: Implement**, add the locale keys (all nine files), then run the specs plus `--run src/app/core/i18n`. Expect PASS.
- [ ] **Step 6: Commit** `feat(web): migrate legacy protected caches and surface recovery`.

### Task 5: Release harness against the built worker

**Files:**
- Create: `src/frontend/tests/ui-audit/release/playwright.release.config.ts`, `release-server.ts`, `legacy-manifest.ts`, `protected-cache-isolation.spec.ts`
- Modify: `src/frontend/package.json` (`"audit:ui:release": "bun x playwright test --config tests/ui-audit/release/playwright.release.config.ts"`)

**Interfaces:**
- `startReleaseServer({ root, manifest: () => object }): Promise<{ origin; setApiDelay(ms: number); close() }>`. Serves `dist/INVENTORY/browser` and `/ngsw.json` from `manifest()`. `/__harness` (blank page, outside navigation fallback) renders fetched bodies into `#out`. `/api/review-tenant` echoes `Authorization` as `{ tenant }` after the configured delay.
- `buildLegacyManifest(root): Promise<object>`: Angular `Generator` over the built files with the config deployed at `fbff57b09` (the three `/api` groups), verbatim.
- Config: `serviceWorkers: 'allow'`, chromium, one worker, no `webServer` (each test owns its server). The spec fails with a clear message if the build output is missing.

- [ ] **Step 1: Write the tests.** Each uses raw page `fetch` without a bypass header (old-code worst case) and asserts body text and `#out` DOM text. `protected_api_not_in_generated_data_groups` (built `ngsw.json`). `legacy_worker_reproduces_cross_shop_reuse` (control: legacy manifest + `ngsw-worker.js` → ShopB gets ShopA after 8 s; proves the harness detects the leak). `shop_b_timeout_never_returns_shop_a` (app worker, 10 s delay → ShopB body). `new_user_offline_never_reads_old_user` (warm user A; `context.setOffline(true)`; user B fetch rejects). `old_worker_open_tab_cannot_serve_protected_body` (tab 1 under the legacy worker, warm ShopA; switch the manifest to current; tab 2 registers `app-worker.js`; tab 1 sees `controllerchange`; tab 1 ShopB with delay → ShopB; no legacy cache holds entries). `cleanup_preserves_unsynced_invoice` (seed `intelibill-offline-sales-queue`/`queued-sales` with Pending and NeedsReview rows before the upgrade; deep-equal after). `assets_offline_api_fails_closed` (offline: `/` serves the shell, `/assets/i18n/en-IN.json` was fetched online and is served, `/api/review-tenant` rejects).
- [ ] **Step 2: Run** `cd src/frontend && bun run build && bun run audit:ui:release`. Expect PASS. Then run it with Task 2's worker reverted locally and expect `shop_b_timeout_never_returns_shop_a` and `old_worker_open_tab_cannot_serve_protected_body` to FAIL (red check, not committed).
- [ ] **Step 3: Commit** `test(web): prove protected cache isolation against the built worker`.

### Task 6: Documentation

**Files:** `docs/frontend-architecture.md` (new § Protected API Cache Isolation), `docs/operations/offline-sales.md` (production QA checklist: `app-worker.js` registers, `ngsw.json` has no API data groups, run `audit:ui:release`), `AGENTS.md` (one Infrastructure bullet).

- [ ] **Step 1: Write the docs.**
- [ ] **Step 2: Commit** `docs: protected API cache isolation`.

## Rollout and rollback

No server data migration. The API header ships with the web build. The first new-code load registers `app-worker.js`, which takes over every tab. Never roll back to wildcard API caching. A rollback that must drop the wrapper serves a build that still sends `ngsw-bypass` and has no `/api` data groups. If cleanup fails, local invoices stay and the shell shows the recovery notice. Never advise clearing all site storage.

# Shared styles and offline pack — phase 0 baseline and spikes

Companion to `2026-11-10-shared-styles-and-offline-pack.md`. Branch `feat/shared-styles-offline-pack`,
measured 10 Nov 2026 from a fresh `bun run build` in `src/frontend` (production configuration) on
`586e4974` before any change of this branch.

## Baseline (before phase 0/1 changes)

Sizes are bytes from `dist/INVENTORY/browser`; gzip is level 9 of each file on its own.

| Measure | Raw | Gzip |
|---|---:|---:|
| First load: `index.html` + the 11 files it references (1 stylesheet, 10 `modulepreload` chunks incl. `main`) | 296,233 | 68,677 |
| of which `index.html` (carries the Beasties-inlined critical CSS, including the `@font-face` rules) | 10,306 | 2,817 |
| of which `main-*.js` | 130,484 | 23,260 |
| Global stylesheet `styles-*.css` (loaded non-blocking; Beasties inlines the critical part) | 47,132 | 10,010 |
| All JS chunks (142 files, workers excluded) | 4,491,106 | 1,175,774 |
| All CSS files (1: the global sheet; component CSS travels inside JS chunks) | 47,132 | 10,010 |
| Fonts (`fonts/`, 8 TTF + LICENSE) | 1,092,513 | — |

Angular's own report for the same build: "Initial total 742.68 kB raw / 186.44 kB transfer" (it
counts every eagerly reachable chunk, not only the modulepreloads in `index.html`).

Build warnings (9, all pre-existing; `bun run build` exits 0):

- `bundle initial exceeded maximum budget` (650 kB warning, 742.68 kB).
- `anyComponentStyle` over 7 kB: `dashboard-page` 7.12 kB, `profit-loss-page` 8.97 kB,
  `sale-return-preview-dialog` 8.89 kB, `customers-page` 10.67 kB, `sales-page` 10.14 kB,
  `inventory-batch-page` 10.63 kB, `offline-reconciliation-page` 10.39 kB, `till-session-page` 8.65 kB.

Repeated rule blocks (`bun run audit:styles:duplicates`, see below): **64** distinct blocks of 3+
declarations appear word for word in 4+ of the 102 component stylesheets. The plan's "59" was an
earlier, looser count; 64 is the ceiling the new audit enforces.

Not recorded in this phase: per-chunk CSS bytes, the `audit:ui:mocked` result and screenshots
(the plan lists them for phase 0; the phase 2 agent should capture them before moving a family).

## Spike A — shared lazy styles: passes

What was built (kept as the real scaffold):

- `src/frontend/src/app/shared/styles/ui-kit/ui-kit-styles.component.ts`: `UiKitStylesComponent`,
  selector `app-ui-kit-styles`, empty template, `ViewEncapsulation.None`, host `hidden` +
  `aria-hidden`, `styleUrls: ['./ui-kit-layer-order.css']`. Rendered as the first element of
  `shell.component.html`. The family list is otherwise empty: phase 2 adds one file per family.
- `ui-kit-layer-order.css`: the only rule is `@layer primeng, ib-kit;`.

Evidence:

1. **Lazy chunk, not on the login load.** With a probe rule in the kit file, the production build put
   the kit CSS (`@layer primeng,ib-kit;@layer ib-kit{…}`) only in `chunk-TMPXYRZN.js`, the chunk that
   also defines `app-shell` (the `shell-routes` lazy chunk). `index.html` does not reference it; `main`
   reaches it only through the `loadChildren` dynamic import.
2. **Cascade order at runtime** (`tests/ui-audit/mock/ui-kit-cascade.spec.ts`, Chromium, mocked shell,
   both audit viewports, 4/4 passed). The document's layer order on `/sales` is:
   `theme, base, primeng, properties, components, utilities, ib-kit`. PrimeNG's first style element
   (`@layer theme,base,primeng`) is *prepended* to `<head>` (`useStyle(..., { first: true })`), so
   `primeng` is declared before Tailwind's layers from `styles.css`; Angular appends component styles,
   so the kit's statement adds `ib-kit` last. The spec injects a PrimeNG-layer rule with the highest
   specificity, a kit rule, and an unlayered rule with the lowest specificity: the kit beats PrimeNG
   and the unlayered rule beats the kit.
3. **Absent on `/login`, survives navigation** inside the shell (desktop: sidebar link to
   `/expenses`, kit still present and still winning). Angular (v21) removes a component's styles when
   its last instance is destroyed, so leaving the shell (print pages, login) drops the kit; that is
   why the plan renders the host in the print pages too.
4. Unit specs: `ui-kit-styles.component.spec.ts` (unscoped, renders nothing, one `<style>` however
   many instances), and `shell.component.spec.ts` checks the host is the shell's first child.

Finding the next phase must know: **the kit also beats Tailwind utilities.** That matches what the
kit replaces (unlayered component rules beat utilities today), so moving a family does not change
which wins against a utility class on the same element. If a later phase wants utilities to win
instead, add `@layer ib-kit;` as the first line of `src/styles.css`: verified in the dev server to give
`theme, base, primeng, properties, ib-kit, components, utilities`. The cascade spec pins the current
choice (`ib-kit` after `utilities`) and must be updated with such a change. The production build was
checked for chunk placement only; the runtime order was checked on the dev server (same DOM
insertion logic).

## Spike B — manifest switch: feasible

Pinned by `src/frontend/scripts/tests/angular-worker-manifest.spec.ts` (5 tests, `bun test`), which runs
Angular's real published worker against in-memory Cache Storage, with a `self.fetch` that answers
`ngsw.json` with a full manifest (compiled from the real `ngsw-config.json` by Angular's `Generator`)
or a lite one (same manifest, `assetGroups: []`, `hashTable: {}`).

Source facts (`node_modules/@angular/service-worker/ngsw-worker.js`, `@angular/service-worker`
21.2.8; the build copies this file to `ngsw-angular-worker.js`):

- Entry (line ~2255): `var scope = self; var adapter = new Adapter(scope.registration.scope, self.caches); new Driver(scope, adapter, new CacheDatabase(adapter));`
- `Driver.fetchLatestManifest(ignoreOfflineError)` (line ~1968):
  `this.safeFetch(this.adapter.newRequest("ngsw.json?ngsw-cache-bust=" + Math.random()))`.
  `Adapter.newRequest` is `new Request(input)`; a relative URL in a worker resolves against the
  worker script, so the request is **`<origin>/ngsw.json?ngsw-cache-bust=0.<digits>`** (GET, no extra
  headers). Match on pathname `/ngsw.json`, not on the full URL.
- `Driver.safeFetch(req)` (line ~2235): `return await this.scope.fetch(req)` — a property lookup on
  `self` at call time. Assigning a wrapper to `self.fetch` in `protected-api-worker.js` before
  `self.importScripts('./ngsw-angular-worker.js')` therefore intercepts every manifest request
  (asset downloads also go through `this.scope.fetch`, so the wrapper must pass those through
  untouched). A 404 answer makes Angular delete all caches and unregister; 503/504 during an update
  check is treated as offline (`checkForUpdate` returns false, no state change).
- Version identity is `hashManifest(manifest) = sha1(JSON.stringify(manifest))`, so full and lite are
  two versions even for the same build.
- `Driver.checkForUpdate()` (line ~2038) → `notifyClientsAboutVersionDetected` → `setupUpdate`
  (line ~2016) → `AppVersion.initializeFully` (every prefetch URL fetched and its SHA-1 checked
  against `hashTable`, retried once with `?ngsw-cache-bust=`) → only then
  `this.versions.set(hash, …); this.latestHash = hash; sync()` → `VERSION_READY` to clients assigned
  to an older version. Failure → `VERSION_INSTALLATION_FAILED`; the old version stays latest.
- Update checks are triggered by: `SwUpdate.checkForUpdate()` (message `CHECK_FOR_UPDATES`), the
  first navigation after the worker starts (idle task `check-updates-on-navigation`), and on every
  worker start that restores state from Cache Storage (`init post-load (update)`).

Behaviour shown by the test:

1. Lite installed first: the only requests are `/ngsw.json?ngsw-cache-bust=0.…` and the page's own
   request; nothing prefetched; offline, an uncached chunk fails (504).
2. Lite → full: `VERSION_DETECTED`, `VERSION_READY`, `OPERATION_COMPLETED(result: true)`; all 8
   prefetched URLs fetched and cached. Offline afterwards, a **new** tab gets every chunk from the
   pack. An **existing** tab stays on the lite version (network) until it activates the update;
   after `ACTIVATE_UPDATE` (`SwUpdate.activateUpdate()`) it reads lazy chunks from the pack
   **without a reload**, because the files are the same build. Phase 3 should call
   `activateUpdate()` after a lite→full `VERSION_READY` when the build hash is unchanged.
3. One prefetched file failing (500): `VERSION_DETECTED`, `VERSION_INSTALLATION_FAILED`,
   `OPERATION_COMPLETED(result: false)`; the lite version is still latest (a lite check answers
   `NO_NEW_VERSION_DETECTED`); the failed version is not remembered, so the next full check downloads
   again and succeeds. **Caveat:** after a failed install the driver is `EXISTING_CLIENTS_ONLY` until
   that worker instance restarts (in-memory state): tabs it has not seen yet are served from the
   network. On a device already on a full pack, a failed update of a *new build* would therefore
   leave a reload while offline to the network until the browser restarts the worker. Phase 3 should
   treat `VERSION_INSTALLATION_FAILED` as "pack possibly not serving" and not report it as healthy.
4. Full → lite: `VERSION_READY` for the lite version, but the pack is **not** deleted at once: Angular
   only runs `cleanupCaches()` when a worker instance initialises, and only drops versions no open
   tab is assigned to. After `ACTIVATE_UPDATE` and a worker restart, the asset caches are gone.
   "Disabling drops the pack" is therefore eventual (next worker start with no tab on the old version).

Other notes for phase 3:

- First install is not atomic: when no state exists, `Driver.initialize()` stores the fetched
  manifest as latest **before** prefetching (`scheduleInitialization`). Only the update path
  (`setupUpdate`) is all-or-nothing. A device whose very first worker install sees the full manifest
  can go offline with a half pack; verification (plan § 3.2 step 4) must not rely on `VERSION_READY`
  alone in that case.
- On non-localhost scopes, initialisation of a restored version runs on the idle scheduler (5 s idle,
  30 s max); the test uses a localhost scope so it runs at once.
- The fallback (register the worker only on offline-enabled devices) is not needed.

## Duplicate-style audit

`bun run audit:styles:duplicates` (`src/frontend/scripts/audit-duplicate-styles.mjs`, test
`scripts/tests/audit-duplicate-styles.spec.ts`). Ceiling in
`scripts/audit-duplicate-styles.ceiling.json`; `--list` prints the blocks, `--json` the report.

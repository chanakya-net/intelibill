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

## Phase 1 — results

Same measurement after phase 0 and phase 1 (`bun run build`, production):

| Measure | Before | After |
|---|---:|---:|
| First load raw / gzip | 296,233 / 68,677 | 294,669 / 68,652 |
| `index.html` raw / gzip (critical CSS now includes the `:root` tokens) | 10,306 / 2,817 | 12,383 / 3,346 |
| Global stylesheet raw / gzip | 47,132 / 10,010 | 43,491 / 9,455 |
| All JS raw / gzip (shell chunk now carries the moved rules and the kit host; `var(--app-…)` is longer than a hex literal) | 4,491,106 / 1,175,774 | 4,505,654 / 1,177,719 |
| Fonts | 1,092,513 (8 TTF) | 374,803 (8 WOFF2) |
| Angular "Initial total" | 742.68 kB | 739.03 kB |
| Build warnings | 9 | 9 (the same files, the same sizes) |
| `audit:styles:duplicates` | 64 | 64 |

What changed:

- `src/styles.scss` deleted (nothing referenced it).
- Rules only signed-in screens need moved from `styles.css` to `ShellGlobalStylesComponent`
  (`src/app/core/layout/shell-global-styles.component.ts` + `shell-global-styles/*.css`, rendered
  second in the shell, unencapsulated and unlayered as before, byte-identical rules). No route outside
  the shell uses these classes (print pages, supplier-return pages, auth pages checked).
  `.inventory-prime-menu` is not used by any template; it moved unchanged and is a candidate for
  deletion.
- Tokens on `:root` in `styles.css`: orange (existing `--app-orange-*` plus 300/800/950), warm brand
  neutrals (`--app-sand-50/300`, `--app-brown-500/600/700/900`, `--app-bronze-600`), stone, slate,
  red, amber (Tailwind v3 hex values, only steps in use), radii (`--app-radius-md/lg/pill`),
  shadows (`--app-shadow-card/panel/overlay`), font families (`--app-font-body/display/heading`).
  Tokens use the existing `--app-` prefix; the `ib-` prefix stays for kit class names.
- 710 exact literal matches replaced in 74 component stylesheets, only in declaration values,
  outside parentheses (gradients, `rgba()`, `var()` fallbacks untouched), never in `$`/`--`
  definitions or values using Sass. Proof of no visual change: every component stylesheet was
  compiled with Sass before and after, the tokens substituted back, and the CSS compared: 102/102
  identical (only Sass's own quote normalisation differs).
- Not tokenised yet: the 16 stylesheets at or near the 7 kB `anyComponentStyle` warning (a first pass
  pushed 8 more files over it because `var(--app-sand-300)` is longer than `#ead7c1`) and the 4
  shared partials. Phase 2 can tokenise them as it shrinks them:
  `core/layout/shell.component.scss`, `customers/components/add-customer-overlay`,
  `customers/pages/customers-page`, `dashboard/pages/dashboard-page`, `discounts/pages/discounts-page`,
  `expenses/components/record-expense-overlay`, `inventory/components/barcode-label-print-dialog`,
  `inventory/pages/inventory-adjustments-page`, `inventory/pages/inventory-batch-page`,
  `sales/components/sale-detail/sale-return-preview-dialog`,
  `sales/pages/offline-reconciliation-page`, `sales/pages/profit-loss-page`, `sales/pages/sales-page`,
  `tills/pages/till-session-page`, `users/components/change-password-overlay`,
  `users/pages/users-page`; partials `_auth-shared`, `_button-mixins`, `_form-control-mixins`,
  `_editor-dialog`.
- `audit:styles:duplicates` resolves `var(--app-…)` to the `:root` value before comparing, so a
  partly tokenised family still counts as one repeated block.
- Fonts: WOFF2 via fontTools 4.68 (+ brotli) in a throw-away virtualenv; glyph order, cmap and table
  set verified identical to the TTFs. `ngsw-config.json` already matched `woff2`; the release server
  already maps `.woff2`; no test named the TTF files. `public/fonts/LICENSE` lists the new names.

### Near-duplicate colours — decide later

Not merged in phase 1 (each would be a visible change, however small). Distances are Euclidean RGB.

| Keep (token) | Near-duplicates still written as literals |
|---|---|
| `--app-brown-900` `#2a1b12` (headings) | `#271911` (40 uses / 19 files), `#241713` (20/4), `#20130e` (14/2), `#241811` (10/3), `#24160f`, `#231714`, `#231811`, `#2a1d19` |
| `--app-sand-300` `#ead7c1` (card borders) | `#e7d6c3` (28/17), `#eadccf` (30/14), `#eedcc8` (9/7), `#e1d1c0` (8/3), `#e4d5c5` (6/1), `#e9d3c2`, `#e3d7c7`, `#ded4c7` (17/4), `#dec0aa` (11/6) |
| `--app-sand-50` `#fffaf4` (card surface) | `#fffaf3` (21/12), `#fffaf5` (14/9, also the body background in `styles.css`), `#fffaf2` (17/3), `#fff9f2` (6/5), `#fffdf8` (8/8), `#fffdf9` (5/5), `#fbf5ec` (16/16), `#f7efe4` (16/16) |
| `--app-orange-50` `#fff7ed` | `#fff5ef` (9/9), `#fff5e7` (13/9), `#fff5e9` (4/4), `#fff2e5` (7/6), `#fff0df` (12/10), `#fff1dc` (11/11) |
| `--app-orange-100` `#ffedd5` | `#ffe8dc` (9/9), `#fde8d4`, `#fff0dc` |
| Eyebrow browns: `--app-bronze-600` `#9b5d20` and `--app-brown-600` `#7a6250` (letter-spacing 0.14em vs 0.16em) | `#8b4510` (14/11), `#a45d19`, `#996031`; greyer eyebrow variants `#786c64`, `#76675f`, `#74665c`, `#76675d` |
| `--app-brown-600` `#7a6250` / `--app-brown-700` `#6f5a48` | `#75604e`, `#826a58` (4/3), `#6f655d` (21/6), `#75665c` (4/3) |
| `--app-red-700` `#b91c1c` (errors) | `#b42318` (16/11), `#b3261e`, `#c1121f` |
| `--app-red-50` `#fef2f2` | `#fff1f2` (18/17, Tailwind rose-50), `#fff3f0` (7/6) |
| `--app-amber-800` `#92400e` | `#8b4510` (see eyebrow), `#934511` |
| `--app-slate-*` | Tailwind gray look-alikes: `#111827` (10/4), `#1f2937` (5/3), `#374151`, `#4b5563` (4/3), `#6b7280`, `#9ca3af`, `#d1d5db` (8/2), `#e5e7eb`, `#f3f4f6`, `#f9fafb` |
| `--app-stone-900` `#1c1917` | `#1f120d` (4/4) |
| `--app-stone-800` `#292524` | `#34281f` (10/7) |
| White | `#ffffff` (48/19) and `#fff` (17/13): no token yet |

Other scales with near-duplicates (no token yet, or only the dominant value tokenised):

- Font sizes (no tokens): `0.78rem` (47), `0.92rem` (45), `0.82rem` (42), `0.8125rem` (42),
  `0.9rem` (39), `0.875rem` (39), `0.75rem` (38), `0.72rem` (36), `0.68rem` (33), `1rem` (30),
  `0.95rem` (28), `0.85rem` (28), `0.7rem` (28), `0.8rem` (25), `0.66rem` (19). A type scale needs a
  merge decision first.
- Radii besides the three tokens: `0.8rem` (25), `0.9rem` (21), `0.85rem` (20), `1.2rem` (19),
  `1.25rem` (17), `0.65rem` (14).
- Card shadows besides `--app-shadow-card` (`0 12px 32px rgba(87, 54, 20, 0.07)`):
  `0 8px 22px rgba(85, 49, 18, 0.07)` (11), `0 10px 30px rgba(78, 49, 20, 0.07)` (11),
  `0 8px 22px rgba(85, 49, 18, 0.06)` (4), `0 8px 24px rgba(87, 52, 20, 0.07)`,
  `0 8px 22px rgba(87, 52, 20, 0.07)`.

## Phase 2 — results

Ten commits on top of phase 1 (`77eb2634`): one preparation commit and one per family.

### Kit structure

- `UiKitStylesComponent` (shell) now lists, after `ui-kit-layer-order.css`: `ib-overlay.css`,
  `ib-page-heading.css`, `ib-summary-cards.css`, `ib-panel.css`, `ib-chips.css`,
  `ib-form-section.css`, `ib-search-shell.css`, `ib-editor-dialog.css` (largest 3.6 kB source).
- `AuthKitStylesComponent` (new, same folder, selector `app-auth-kit-styles`) delivers
  `ib-auth.scss` (6.1 kB source, uses the button mixin) to the four auth pages outside the shell
  (login, forgot password, reset password, verify email), which render it as their last element.
  Its CSS lands in one shared lazy chunk, not in the shell chunk; signed-in screens never load it.
- **Preparation:** the unlayered `h1`–`h6` and `p` resets in `styles.css` moved into
  `@layer ib-base`, declared after Tailwind's layers. Without that, every kit class that sets a margin
  or font on a heading or paragraph would lose to `h2 { margin: 0 }` (unlayered beats layered),
  where the component rule it replaces used to win on specificity. The resets still beat PrimeNG and
  Tailwind utilities and still lose to every component rule. Layer order is now
  `theme, base, primeng, properties, components, utilities, ib-base, ib-kit`; the cascade spec pins
  it (and that a kit margin beats the `ib-base` reset). Checked in the production build: Beasties'
  inlined critical CSS keeps `@layer ib-base` after Tailwind's layer statements.

### Families moved

| Family | Kit classes | Components |
|---|---|---|
| Overlay frame | `ib-overlay`, `__backdrop`, `__card`, `__header`, `__eyebrow`, `__title`, `__description`, `__close`, `__form`, `__actions`, `__error`, `__busy` | bank account, edit customer, add/edit supplier, supplier payment, add/edit shop user overlays (frame + content); expense correction (content only; its own frame differs) |
| Page heading | `ib-page-title`, `ib-eyebrow` (+`--large` 0.7rem), `ib-subtitle` (+`--measure` line-height/max-width) | customers, discounts, expenses, users, account security, stock adjustments, profit and loss, inventory, services pages (title); + purchase order detail and till session (eyebrow only) |
| Summary cards | `ib-summary-grid`, `ib-summary-card` (+ four tones), `__label`, `__value` | customers, discounts, expenses, users, purchase order detail pages, stock adjustment summary, dashboard KPI cards (card only) |
| Directory panel | `ib-panel`, `__header`, `__title`, `__metrics`, `__surface` | customers, discounts, expenses, users, stock adjustments, purchase order detail (not the title), account security sections |
| Meta / metric chips | `ib-meta-row`, `ib-meta-chip` (+`--primary`), `ib-metric-chip` | the seven pages of the panel family |
| Form section | `ib-form-section`, `ib-section-label`, `ib-form-grid` (+`__wide`), `ib-field`, `ib-checkbox-field`, `ib-error-banner` | add product, edit item, add/edit service, stock adjustment row, discount conditions, discount target items, discount rule dialog |
| Search shell | `ib-search-shell` | customers, discounts, expenses, users filter bars |
| Editor dialog | `ib-editor-dialog-*` (grid, header + `__eyebrow`/`__title`/`__subtitle`, form, summary, section, field, actions, preview, hint) | add customer, record expense, change password, supplier detail, batch list page, barcode label dialog (the `styles` mixin's class-only rules) |
| Auth pages | `ib-auth-*`, `ib-lang-pick`, `ib-form-busy` | login, forgot password, reset password, verify email |

Where tests or remaining component rules use the old class names (summary cards, panel, chips,
form section, editor dialog, auth), templates keep the old name beside the `ib-` one; the overlay
frame, page heading and search shell classes were renamed and the plain locators in
`bank-accounts`, `customers` and `suppliers` mocked specs and the bank account overlay unit spec
were updated. Variants stayed in the components as small unlayered overrides (wider invite card,
expense correction title size, extra `min-width`/`overflow-wrap` on the stock adjustment summary and
panel, meta chip `gap` on customers/expenses, wrapping meta chips on discounts, every page's own
breakpoints).

### Left in place, and why

- **Rules a kit rule would no longer beat.** A kit rule is layered, so it loses to *any* unlayered
  rule, whatever its specificity. Kept for that reason: the overlays' `label`/`label span` and
  `.toggle-row` (a `label` element rule would beat the toggle row); the purchase order page's panel
  title (`PurchaseOrderBuilderPageComponent` is `ViewEncapsulation.None` and its global `h2` rule
  applies while the builder is open); the discount conditions form's checkbox label (its own
  `.field label` rule would win); the editor dialog's `:host ::ng-deep .editor-dialog` block and
  `overlay` mixin, and the auth partial's input and spinner rules, which reach PrimeNG-rendered
  elements through `pt` classes where component scoping decides what matches.
- **Not identical:** the other eyebrows (0.14em brown, 0.22em red, 0.74rem supplier), subtitles
  (greyer `#6f655d`, 1rem supplier, 0.875rem till), titles (2.6rem purchase order, credit notes,
  supplier, till), the supplier page's panel/summary/meta chips, the shop and default-store
  overlays (different frame), the PrimeNG `p-dialog` editor header and footer (`.editor-header`,
  the `::ng-deep` dialog panel rules: still duplicate blocks #5–#11, #20 and the input block #4),
  the page backgrounds and hero cards (#0, #2, #3: not in the plan's family list), the supplier and
  sales search boxes.
- **Kept on purpose:** `inventory-batch-page` (unencapsulated; its global `h1`/`.eyebrow` rules
  apply while it is open) and `inventory-batches-list-page`'s page heading (shares the file with
  the editor dialog; its `.eyebrow`/`.subtitle` page rules are why the dialog header uses its own
  kit classes); the datatable header rules inside panel surfaces (`::ng-deep`, left alone per plan).
- **Auth footer:** `.auth-footer` is not used by any auth template; it stays in the partial.

### Numbers

Same method for both columns (`bun run build`, production; gzip level 9 per file; first load =
`index.html` + the files it references; JS excludes the four worker files).

| Measure | Phase 1 (`77eb2634`) | Phase 2 |
|---|---:|---:|
| All JS raw / gzip | 4,505,654 / 1,173,262 (142 files) | 4,448,123 / 1,164,398 (143 files) |
| First load raw / gzip | 294,669 / 68,370 | 294,701 / 68,402 (`@layer ib-base` wrapper in the critical CSS) |
| Global stylesheet raw / gzip | 43,491 / 9,395 | 43,507 / 9,400 |
| Angular "Initial total" | 739.03 kB | 739.05 kB |
| Build warnings | 9 | 9 (same files; customers page 10.67 → 8.08 kB, profit and loss 8.97 → 8.71 kB, till session 8.65 → 8.54 kB) |
| `audit:styles:duplicates` | 64 | 27 (ceiling lowered to 27; the audit now sees 103 stylesheets because `ib-auth.scss` is one) |

(The phase 1 table's gzip figures used a different gzip setting; the raw figures match.)

### Verification

- **Declarations, per family:** every touched stylesheet was compiled with Sass before and after the
  move (tokens resolved, selector lists split), old class names mapped to the kit classes, and the
  result compared with the kit's declarations: every removed declaration exists in the kit for the
  same element (or stays in the component as an override), and the kit adds nothing a component
  did not have. A second check lists every remaining unlayered rule on a class kept beside a kit
  class that sets a property the kit also sets; each one was confirmed to be a later override that
  won before too (breakpoints, variants). That check caught the discount conditions form's checkbox
  label, which stays in the component.
- **Computed styles in the browser:** a temporary probe (not committed) recorded `getComputedStyle`
  of every element (custom properties excluded, property order normalised) and a full-page
  screenshot at each checkpoint of 17 mocked specs (login, public pages, bank accounts, customers,
  dashboard, discounts, expenses, stock adjustments, batch entry, batch list, inventory list, profit
  and loss, purchase order detail, services, shell layout, suppliers, users), on `77eb2634` and on
  this branch: 707 checkpoints. Differences that were not also run-to-run noise on the same commit
  were re-recorded with full property dumps. Result: no element's computed style changed. Remaining
  differences: the added `<app-auth-kit-styles hidden>` element and the kit `<style>` elements; a
  chart canvas width (chart.js sizing, 273px vs 273.2px, also varies between runs of one commit); and
  on the users page an "Unable to load invitations" banner present in one run and not the other (a
  race between the directory and the unmocked invitations request; the same request fails the same
  way on both commits in isolation, and every other element is identical).
- **Screenshots:** 236 of 252 identical byte for byte; the 16 others are the animated online
  indicator, spinners, toasts, chart canvases and the users banner above.
- **Mocked audit (`bun run audit:ui:mocked`), base `586e4974` vs branch:** 121 vs 124 failures
  before the test fixes, all pre-existing apart from (a) the cascade spec's login assertion (the
  login page now has an `ib-kit` layer, from the auth host: the assertion now checks that the
  shell kit is absent and the auth kit present), (b) expenses and users specs that located the
  correction and shop user overlays by the old `.error-message`/`.close-button`/`.overlay-card`
  (locators updated), (c) one users locale test that timed out on the same locator. After the
  fixes those specs were re-run: the cascade spec and the expenses fallback test pass, the users
  dense test passes, and the remaining expenses/users failures are the ones that also fail on base
  (translation text and fixture mismatches). Runs were done with other jobs in parallel; flaky
  timing failures were compared by name only.
- `bun run build`: exit 0, the same 9 warnings. `bun run test --watch=false`: 299 files, 2,946 tests
  passed; 71 Bun script specs passed. `bun run audit:styles:duplicates`: 27, ceiling 27.

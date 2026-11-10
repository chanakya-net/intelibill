# Shared styles and on-demand offline pack — plan

Date: 2026-11-10. Scope: `src/frontend` (Angular web app) only. Status: proposed, nothing built.

Two goals:

1. Stop repeating the same styles across component files; keep one shared copy that is only
   downloaded when a screen needs it, so the first load stays small.
2. Download the app's files for offline use only when the user turns offline on for the device,
   keep them complete, and refresh them periodically.

## 1. What the analysis found

Numbers are from the production build in `dist/INVENTORY/browser` (10 Nov 2026) and the source tree.

### Styles

| Fact | Value |
|---|---|
| Style files | 107 component `.scss` (379 KB source), 1 global `src/styles.css` |
| Shared partials | 4: `_button-mixins`, `_form-control-mixins` (mixins only), `_auth-shared` (emits rules), `_editor-dialog` (2 large mixins) |
| Global stylesheet, built | `styles-*.css` 47 KB (10 KB gzip), render-blocking |
| First load (index + preloaded chunks + styles) | 333 KB raw, 76 KB gzip; budget warning is 650 KB |
| Rule blocks (3+ declarations) repeated word for word in 4+ files | 59 distinct blocks, about 36 KB of repeats |
| Same selector **and** body in 4+ files | 45 |
| Distinct hex colours in component styles | 431 distinct, 2,105 uses; almost no CSS variables |
| `::ng-deep` uses | 210 |

Findings:

- **Component styles are already lazy.** Every route uses `loadComponent`/`loadChildren`, and a
  component's CSS travels inside its JS chunk. The cost of duplication is total download and
  offline pack size, not first-load size.
- **Repeated families** (files that mention each): overlay frame — `.overlay`, `.overlay-backdrop`,
  `.overlay-card`, `.overlay-header`, `.overlay-form`, `.submit-overlay`, `.close-button`,
  `.actions` (26); summary cards (23); directory panel (17); meta chips (17); form section (13);
  metric chips (12); search shell (10); the Georgia page heading `h1`/`h2` (37); `.eyebrow` in three
  near-identical variants (letter-spacing 0.14em / 0.16em, two browns).
- **Shared partials still duplicate output.** `_auth-shared.scss` (6.7 KB) is compiled into each of
  4 auth pages, and the `_editor-dialog` mixins (8.3 KB) into 6 components: shared in source,
  repeated in every chunk.
- **`src/styles.scss` is dead.** `angular.json` builds `src/styles.css`; the `.scss` twin is an
  older, diverged copy.
- **The global sheet carries signed-in-only rules**: shell panel-menu and drawer overrides,
  `.inventory-prime-menu`, checkout button contrast. The login page pays for them.
- **Fonts are 8 TTF files, 1.1 MB.** WOFF2 would be roughly 60% smaller. PrimeIcons ships five
  formats (630 KB) though browsers only fetch the 35 KB WOFF2.

### Offline

| Fact | Value |
|---|---|
| Worker registration | every device, 30 s after the app is stable (`app.config.ts`) |
| `ngsw-config.json` `app` group | `installMode: prefetch`, all `/*.js` + `/*.css` |
| What that downloads in the background | 146 JS chunks + styles: 4.58 MB raw, about 1.2 MB gzip, on **every** device |
| `assets` group (fonts, i18n, icons) | lazy; 9 locale files total 2.4 MB |
| "Enable offline" action | `OfflineSalesDeviceEnablementService.enableForShop`: data snapshot + invoice lease only |
| Update checks | none. `SwUpdate` is not used anywhere; a new version is only noticed on a fresh navigation |
| Snapshot refresh timer | none found |
| Storage persistence request (`navigator.storage.persist`) | none |

Findings:

- Today's behaviour is the opposite of the goal: the whole app is downloaded for everyone whether or
  not offline is on, and the enable button does not check that the app files are actually cached.
- A device that is enabled can still fail offline: nothing verifies the pack, nothing asks the
  browser to keep the storage, and the fonts/locale file are only cached if they happened to be
  requested.
- The worker at `ngsw-worker.js` is Intelibill's wrapper (`service-worker/protected-api-worker.js`)
  around Angular's worker. It keeps `/api` and `/hubs` out of every cache. That rule and the
  registered URL must not change.

## 2. Decisions (my calls — say if any is wrong)

1. **Whole app in the offline pack**, not only the billing screen. One version of everything,
   about 1.2 MB gzip plus fonts and locale files. Only `/sales/new` and invoice printing are usable
   offline today, but a partial pack means hand-maintaining a chunk list that breaks silently.
2. **Devices without offline turned on download nothing in the background.** They use ordinary
   browser caching (hashed files are already served immutable for a year).
3. **Updates never interrupt a sale.** A new version downloads in the background and is applied on
   the next app start, or when the user presses an "Update ready" control.
4. **"Update periodically" covers both** the app files and the offline data snapshot.
5. **Class names in the shared kit get an `ib-` prefix**, so shared rules can never leak into a
   component that happens to use a generic name such as `.actions`.

## 3. Design

### 3.1 Shared styles

Three layers, each loaded at a different time:

| Layer | Contents | Loaded |
|---|---|---|
| Tokens | CSS custom properties: colour palette, radii, shadows, type scale | first load, inside `styles.css` (small) |
| UI kit | The repeated families as `ib-` classes, one file per family | first time a signed-in screen opens |
| Component styles | What is genuinely unique to the component | with the component, as today |

**How the kit is delivered.** A component with an empty template and
`ViewEncapsulation.None`, `UiKitStylesComponent`, lists the family files in `styleUrls` and is
rendered once by `ShellComponent` and by the print pages that sit outside the shell. Angular adds
its styles to the document when the component is first created and builds them into the shell's
lazy chunk. This gives, without extra machinery:

- one copy of each rule in the build;
- nothing on the login page;
- a hashed file that the offline pack includes automatically;
- no flash of unstyled content, because the styles are added before the shell renders.

Each family file stays under the 7 kB `anyComponentStyle` warning. Families used by one feature
area only (for example the editor dialog) get their own host component rendered by that area, so
they load with it.

Auth pages get the same treatment with `AuthKitStylesComponent`, replacing the four compiled copies
of `_auth-shared.scss`.

Fallback if the spike in phase 0 shows a problem (style removal on destroy, budget, ordering): a
second stylesheet bundle in `angular.json` (`inject: false`) loaded by a route resolver that waits
for the `<link>` to finish. It needs its own hashing and worker entry, which is why it is second
choice.

**Cascade order.** PrimeNG is in `@layer primeng`. The kit goes in a named layer declared after it;
component styles stay unlayered, so a component always wins over the kit and the kit over PrimeNG.

### 3.2 Offline pack

Keep Angular's worker and its all-or-nothing install, and choose **which manifest it sees**.

- The build writes two manifests: `ngsw.json` (full: everything prefetched, fonts and locale files
  included) and `ngsw-lite.json` (no asset groups, navigation always from the network). The lite one
  is derived from the full one by `scripts/finalize-service-worker.mjs`.
- The wrapper worker reads a device flag from Cache Storage (`intelibill:offline-pack:state`, not an
  `ngsw:` name) and, before loading Angular's worker, wraps `self.fetch` so that Angular's request
  for `ngsw.json` is answered with the lite manifest when the flag says **off**.
- **Flag missing means full.** A device already enabled before this release keeps its pack even if
  the worker updates before the page has written the flag. The page writes an explicit `off` on
  devices with no shop enabled.
- The flag is on when any shop on the device has offline enabled.

Why this route: Angular's worker only switches to a version after every prefetched file has
downloaded and matched its hash, and keeps the old version until then. A page-driven "fetch every
URL" warm-up has no such guarantee and leaves a window where a device can go offline with half a
pack. Registering the worker only on enabled devices would also work, but it disturbs the cache
isolation and migration code that expects the wrapper everywhere; it is the fallback.

**Enable flow** (`enableForShop` gains a step before the snapshot):

1. Check free space (`navigator.storage.estimate`) and request `navigator.storage.persist()`.
2. Write flag `on`; call `SwUpdate.checkForUpdate()`.
3. Wait for `VERSION_READY` (success) or `VERSION_INSTALLATION_FAILED` / timeout (fail with a new
   reason `APP_PACK_INCOMPLETE`; flag goes back to `off` unless another shop needs it).
4. Verify: every URL in the full manifest is present in the worker's cache; record manifest hash and
   time in the device settings.
5. Continue with the existing snapshot and lease steps. Enabled state is saved only when all pass.

The enable button shows progress and a clear failure message. Disabling the last shop writes `off`;
the next check drops the pack.

**Periodic update** (new `OfflinePackUpdateService`, started from the app initializer):

- `checkForUpdate()` on start, every 30 minutes while the tab is visible and the API reachable, on
  the `online` event and when the tab becomes visible again.
- `VERSION_READY` → show "Update ready"; apply through `ShopContextService.prepareToLeave()` so no
  cart or draft is lost; otherwise the next app start picks it up.
- `unrecoverable` → tell the user to reload while online.
- On enabled devices the same timer refreshes the data snapshot when it is older than a set age
  and checks the invoice lease expiry, reusing `OfflineSalesSnapshotSyncService`.
- The device settings show "Offline files: version, last checked, last updated".

## 4. Phases

Each phase is its own branch and PR, and leaves the app releasable.

### Phase 0 — proofs and baseline (no product change)

- Record baseline: first-load size, total JS/CSS, per-chunk CSS bytes, `audit:ui:mocked` result
  (81 known failures on main), screenshots of the affected screens.
- Spike A: `UiKitStylesComponent` — confirm styles land in the shell chunk, are absent from the
  login load, survive navigation, and respect layer order.
- Spike B: manifest switch — confirm Angular's worker requests the manifest through `self.fetch`,
  installs the full manifest atomically after a lite one, emits `VERSION_READY`, and drops the pack
  when switched back; confirm lazy chunks load without a reload after the switch (same build).
- Add a script that fails CI when a rule block repeats in more than N component files, set at the
  current count so it can only go down.

Exit: both spikes pass, or the fallback for the failing one is adopted and this document updated.

### Phase 1 — tokens and clean-up

- Delete `src/styles.scss`.
- Add tokens to `styles.css`; map the 431 colours to the palette, listing near-duplicates
  (for example the two eyebrow browns) for a keep-or-merge decision. No visual change intended:
  only exact matches are replaced in this phase.
- Move shell-only and checkout-only rules out of `styles.css` into the shell and checkout.
- Convert fonts to WOFF2 (keep TTF out of the build).

### Phase 2 — UI kit, one family per PR

Order by repeat count: overlay frame → page heading and eyebrow → summary cards → directory panel →
chips → form section → search shell → editor dialog → auth pages.

For each family: write the kit file, switch templates to `ib-` classes, delete the copies, run the
component specs, the mocked UI audit and a screenshot diff of every screen that used it.
`::ng-deep` rules are left alone unless they are part of the family.

### Phase 3 — offline pack on demand

- Two manifests from the build; `ngsw-lite.json` added to the revalidated files in
  `server-cache-policy.ts`.
- Wrapper worker: flag read, manifest switch, missing-flag-means-full. Tests beside
  `scripts/tests/protected-api-worker.spec.ts`.
- Page: flag reconciliation on start, enable/disable flow, verification, new failure reason,
  progress in both enable entry points (`shell.component.ts`, `manage-shop-overlay.component.ts`),
  translations in all 9 locale files.
- `ngsw-config.json`: fonts and locale files move into the prefetched group of the full manifest.

### Phase 4 — periodic update

- `OfflinePackUpdateService` as in 3.2, with the status line in device settings.
- Snapshot age refresh and lease check on the same schedule.

### Phase 5 — release proof and docs

- Extend `tests/ui-audit/release`: fresh device downloads no chunk beyond the route it opens;
  enabling downloads the full pack; offline reload of `/sales/new` and an offline invoice print
  work; a second build is picked up by the periodic check and applied without losing a cart; an
  upgrade from the current release keeps an enabled device's pack; `protected-cache-isolation`
  still passes unchanged.
- Update `docs/frontend-architecture.md` (new sections: Shared Styles, Offline Pack) and the
  frontend notes in `CLAUDE.md`/`AGENTS.md`.
- Verify: `bun run test --watch=false`, `bun run build`, `bun run audit:ui:release`; budgets and
  warnings unchanged or lower.

## 5. Risks and how each is covered

| Risk | Cover |
|---|---|
| Visual regressions from moving styles | One family per PR, screenshot diff per affected screen, mocked audit compared to the main baseline |
| Kit rule overrides a component | `ib-` prefix, kit in a layer below unlayered component styles |
| Angular changes how its worker fetches the manifest | Spike B, a unit test pinning it, release audit; fallback is registering the worker only on enabled devices |
| Already-enabled device loses its pack during rollout | Missing flag means full; release test for the upgrade path |
| Device goes offline during an update | Angular's worker keeps the old version until the new one is fully cached |
| Browser evicts the pack | `storage.persist()` at enable; verification on each start while online; clear "offline files incomplete" state |
| Update reloads mid-sale | Applied only at app start or through `prepareToLeave()` |
| `/api` or `/hubs` answers cached | Wrapper's protected-request listener untouched; flag cache uses a non-`ngsw:` name; isolation spec stays in the release run |
| Old tab on an older build | It keeps registering `ngsw-worker.js`, which is still the wrapper |
| Safari: no `persist()` guarantee, 7-day eviction for unused sites | Stated in the device status; pack re-verified on start |

## 6. Expected results

- Background download on a device without offline: from about 1.2 MB gzip to none.
- Global stylesheet smaller (shell and checkout rules moved out); fonts roughly 60% smaller.
- Repeated style output removed: about 36 KB of word-for-word repeats, plus the compiled copies of
  the two shared partials (about 27 KB auth, up to 50 KB editor dialog) — source bytes, to be
  re-measured against the phase 0 baseline.
- Enabling offline is all-or-nothing and verified; enabled devices pick up new versions within
  about 30 minutes of being online.

## 7. Not in this plan

Mobile app; making more screens work offline; dark mode or a visual redesign; replacing Tailwind or
PrimeNG; background sync while the app is closed.

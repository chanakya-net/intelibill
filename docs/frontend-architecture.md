# Frontend Architecture

> High-level architecture reference. For implementation patterns see [`docs/architectural_patterns.md`](architectural_patterns.md). For build commands and dependencies see [`AGENTS.md`](../AGENTS.md) at repo root.

## Stack

| Concern | Choice |
|---|---|
| Framework | Angular 21 — standalone components |
| Rendering | Client-side only — the app is served as static files by a small Express server that proxies `/api` and `/hubs` |
| PWA | Service worker + web manifest (`ngsw-config.json`) |
| UI | PrimeNG 21 + Tailwind CSS 4 |
| State | NgRx 21 — root store + feature providers |
| Data / Auth | HttpClient with interceptors (JWT bearer + 401 refresh) |
| i18n | Transloco — HTTP-loaded JSON translation files (locale files under `public/assets/i18n/`) |
| Package manager | **Bun only** (1.3+) — no npm/yarn |

## Folder Structure

```
src/frontend/src/app/
├── app.config.ts          # Global providers: router, HTTP interceptors, NgRx, PrimeNG, Transloco, SW
├── app.routes.ts          # Public auth routes + guarded shell
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
│   ├── auth/              # Login, register (multi-step routes), email verification
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

1. Public routes: `/login`, `/register/*`, `/auth/verify-email`, `/invitations/accept`
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
| `BarcodeDetectorService` | Wraps browser BarcodeDetector API for scanning (barcode cache + lookup) |
| `CameraStreamService` | Manages camera access for barcode/inventory capture |
| `AudioService` | Plays feedback sounds (e.g., scan beep, transaction confirm) |

## IndexedDB Storage (core/storage)

| Service | Store | Purpose |
|---|---|---|
| `ProductCatalogIndexedDbService` | `product-catalog` | Offline item catalog synced from backend stream |
| `SalesCartIndexedDbService` | `sales-cart-by-owner` (DB v2) | Persists the in-progress POS cart per user + shop (`ownerKey`); the shop-keyed v1 `sales-cart` store is no longer read |
| `InventoryDraftIndexedDbService` | `inventory-draft` | Persists in-progress inbound stock entry |
| `CheckoutIntentIndexedDbService` | `checkout-intents` | Frozen online checkouts (`prepared → sending → uncertain → confirmed/rejected`), stored before the sale POST; `CheckoutIntentService` replays the stored request on retry and never deletes an unresolved intent |

## Offline Reconciliation Console

`/sales/offline-reconciliation` (`features/sales/pages/offline-reconciliation-page`, data via
`OfflineReconciliationService`; linked from the menu and sales history). Every role sees it; actions
follow the server's `availableActions`. Backend rules: `docs/backend-architecture.md` § Offline Reconciliation.

- **Device queue** is live from `OfflineSalesQueueSyncService.unsentSales` for the active shop and this
  browser's device id: Pending/Failed invoices are transport retries, NeedsReview opens its case, and
  another user's invoices are listed as awaiting their issuer.
- **Send to server for review.** A NeedsReview row the signed-in user may submit (`isSubmittableBy`)
  whose case kept no original invoice (`hasOriginalEnvelope` false, learnt from the case detail) or that
  has no case at all offers a one-time resend: `resubmitForReview` sends that one record through the
  normal sync request; if the request fails the record goes back to NeedsReview exactly as it was, so
  automatic and bulk retries (`isOfflineSaleSyncRetryable`) still never resend held invoices.
- **Issuer stamping.** New queued invoices carry `issuedByUserId`; queue sync submits only the
  signed-in user's invoices (and legacy unstamped ones).
- **Cases** show a status filter, paging, the frozen invoice, reason, differences, people ("unknown
  user" for non-members), the invoice number reservation window, and the audit trail, plus a resolve
  form. State is shown only from the last server response; offline display never claims a resolution.
  When no action is offered the note is role-specific: owners/managers read that none is available,
  staff that an owner or manager may have more options.
- **Link actions** (`LinkStockCorrection`, `LinkExistingSale`) pick from the server's `linkCandidates`
  in a select (adjustment number, quantity and date, or invoice number and date); there is no free-text
  id, and only an offered candidate is ever sent. With none, the form says what to do (record a Found
  stock or Other gain increase of at least the missing quantity on the batch, or wait for the sale to
  post) and offers "Reload case".
- **Support bundle.** Anyone can export this browser's undelivered invoices (Pending, Syncing, Failed,
  NeedsReview, any issuer) for the active shop (`buildSupportBundle`): parts of at most 100 invoices and
  1,900,000 UTF-8 bytes, each a complete bundle with its own checksum over its `invoices` text, named
  `offline-recovery-<deviceId>-<yyyyMMddHHmm>.json` or `...-part<N>-of-<M>.json`. Every part is
  downloaded, then the page asks the user to confirm the files were saved; only that confirmation
  records the export (`lastSupportExport.v1:<shop>:<device>` in localStorage: every part's checksum and
  the client sale ids). Discarding records nothing. Owners/managers import one file at a time; imports
  always open a review.
- **Hand-off notice** covers the active shop on this browser only, and says so: it is safe to clear or
  retire when every unsynced invoice of this shop and device is in the last confirmed export (or there
  are none). Invoices of other shops billed on the same browser are not counted; the user must switch
  to each shop and check there.

## GST Documents and Tax Profile

- **Prints from the snapshot.** The A4 and thermal invoices (`sale-invoice-a4`, `sale-invoice-thermal`,
  helpers in `sale-invoice-document.util.ts`) render an issued sale only from `sale.document`: seller and
  buyer blocks, place of supply, the title for its document type, HSN/SAC, UOM, taxable value, the
  CGST/SGST/UTGST/IGST rows and declarations. Live shop details are used only for a sale without a
  document, which carries the "posted without a GST document" note (the API reports every such sale as
  legacy, including an offline sale posted under review). A redeemed credit note counts toward part
  payment; the settlement row lists the credit notes redeemed on this sale (`creditNoteRedemptions`).
- **Tax profile.** `shop-tax-profile-form` (manage shop, owner only) saves through `PUT
  shops/{id}/tax-profile` on its own; the create-shop overlay sends the same fields as `taxProfile`. The
  manage-shop details step hides the GSTIN and sends the shop's current one back, since the server
  refuses a different GSTIN once a profile is declared. Both forms keep unsaved edits when the other's
  save refreshes the shop details.
- **Customer GSTIN.** Add/edit customer validate the GSTIN check digit and state prefix and that it
  matches the chosen state. Edit sends `''` to clear a GSTIN or state: the server keeps a field that is
  absent or null.
- **Errors.** `Invoice.*` refusals map through `sales/utils/invoice-error-keys.ts`; shop tax codes through
  `shops.effects.ts`.

## Offline Invoice Printing

- **Frozen at finalization.** `OfflineSaleFinalizationService` builds the queued sale's `document` (snapshot
  version 2) with `buildOfflineInvoiceDocument`, a port of the server's `InvoiceDocumentBuilder.Build`, from
  the seller profile the consumed lease froze (`InvoiceLeaseDto.sellerProfile`) and the snapshot customer's
  GSTIN, state and address. Prints and reprints render only that document. Both builders run
  `tests/contracts/offline-invoice-document-v2.json`, so the copy handed over matches the document the
  server issues on sync. A sale that cannot be issued is refused with the server's `Invoice.*` code and
  its number goes back to the lease; so is a lease with no profile (re-enable offline billing online; the
  POS says so while online) and a customer missing from the snapshot.
- **Legacy rows** (queued before documents) print the priced bill with a note that it is not a GST
  document copy and a link to reconciliation; no seller identity is taken from the live shop.
- **Access.** `sales/:saleId/print` uses `offlineInvoicePrintGuard`: a server sale needs normal sign-in;
  `offline=1` may also open while the API is unreachable, under the offline grace, when
  `OfflineInvoiceAccessService.read` grants that invoice: the active session's own shop and device, its
  issuer (or, on a live session only, an owner or manager), re-checked after the IndexedDB read. A refusal
  from a reachable server sends the user to sign in.
- **Printing** opens the dialog once after render and font loading; "Print again" only reopens it. The
  queue delivery state is shown beside the invoice on screen and never printed. A blocked print window
  leaves a link in the offline confirmation.

## Shop Context Transitions

A tab works as one owner, `ScopeOwner { userId, shopId }`, held by `ShopContextService` (`core/context`)
with a tab-local `epoch`.

- **One write path.** Every session `AuthSessionService` installs (sign-in, switch and shop creation via
  `applyAccessScope`, renewal, `SESSION_UPDATED`/`SESSION_CLEARED` from another tab, sign-out) goes through
  `ShopContextService.transition`. A change of owner, in order: bumps the epoch, hides routed content in the
  shell (`transitioning`), runs `onDetach` hooks while the old session is still installed, dispatches
  `ShopContextActions.scopeChanged` (`scopeResetMetaReducer` resets every slice but `appShell`, `httpUi` and
  `authRegistration`), and installs the session. Between two signed-in owners the app then restarts at `/`
  (`AppReloadService`, after the detach saves settle or `RESTART_SAVE_WAIT_MS`), so routes and guards re-run
  for the new role. A sign-out does not restart: the shell shows `shell.signedOut` and navigates to `/login`
  itself when no caller did. Renewals and preference saves that keep the owner change nothing else.
- **Switching.** `ShopsEffects.switchActiveShop$` and `createShop$` first await `prepareToLeave()`: it refuses
  while an owner-bound write holds a `beginMutation()` lease (`errors.shops.switchBlockedByPendingOperation`)
  or a non-empty draft cannot be saved under its owner (`errors.shops.switchDraftSaveFailed`).
  `applyAccessScope` refuses an answer whose `scopeVersion` is older than the installed token's
  `scope_version` claim, or that was requested from another device session.
- **Late answers.** `authInterceptor` records the epoch a request left under. For API requests other than the
  session-installing ones (login and confirm endpoints, token refresh/revoke, `shops/switch`, `POST shops`), an
  answer arriving after the epoch moved completes without a value; requests carrying `REQUEST_SCOPE`
  (`scopedRequestContext(owner)`) get `StaleScopeError` instead, and are refused unsent
  (`ScopeMismatchError`) whenever their owner is not the tab's. A 401 for a scope already left is neither
  renewed nor turned into a sign-out, and a renewal that comes back in another shop or account reports the
  request's failure without clearing that (valid) session. The catalog stream (`fetch`, outside the
  interceptor) checks the epoch after every await and cancels itself.
- **Owners of work.** `SaleCartStateService` keeps the owner its rows were loaded for, saves under it, and
  detaches on every transition (load dropped, rows saved then cleared). A failed save, or a draft that could
  not be read (never overwritten), shows `sales.newSale.draftSaveFailed`. Checkout intents, bulk-intake
  chunks and offline-sync batches are sent with their owner; checkout and intake also hold a mutation lease.
  An answer lost to a move leaves the intent `uncertain` (`recordSaleDetached`), the intake submission stored,
  or the offline invoices `Failed` (`offline_sync.scope_changed`, retried by their issuer) — never another
  owner's draft rewritten or a verdict applied under another token.

## Protected API Cache Isolation

An answer under `/api` or `/hubs` belongs to the user and shop that asked for it, and every cache keys what it
stores by URL. Builds up to `fbff57b09` declared an `api-fresh` data group over `/api/**` (1 h, 8 s timeout,
`ignoreVary`), so a slow request in shop B was answered with shop A's stored body. Nothing in the service worker
may hold a protected answer now:

- **Worker.** `bun run build` runs `build:worker` (`scripts/finalize-service-worker.mjs`) after `ng build`. That
  step moves Angular's worker to `ngsw-angular-worker.js` and publishes `service-worker/protected-api-worker.js`
  as `ngsw-worker.js`, the URL every build has registered. The wrapper's `fetch` listener runs before Angular's.
  For a same-origin request under `/api/` or `/hubs/`, it calls `stopImmediatePropagation()` without
  `respondWith`, so the browser fetches it from the network and no app version's rules see it. On activation it
  empties and deletes the legacy protected caches. It answers the `PROTECTED_WORKER_QUERY` message, which an
  earlier worker at the same URL cannot. A tab keeps the app version it loaded with, and that version's data
  groups, until it reloads, so the configuration change alone would leave an open old tab exposed. Once the
  wrapper is installed, Angular's code in it takes control of every open tab (`skipWaiting` + `clients.claim`),
  old ones included. Keeping the URL is what makes this stick: the browser's own update check on any in-scope
  navigation installs the wrapper, and an older page that registers `ngsw-worker.js` again cannot reinstate
  Angular's bare worker.
- **Configuration.** `ngsw-config.json` has no data groups, and `navigationUrls` excludes `/api/**` and `/hubs/**`.
  `scripts/tests/ngsw-config.spec.ts` compiles the config with Angular's own `Generator` and fails if any rule
  covers a protected URL. A future per-feature API cache needs its own identity and tenant design; generic
  authenticated GET caching never comes back.
- **Requests.** `authInterceptor` sends the bearer token only to `API_BASE_URL` (`classifyApiRequest`); anything
  else goes out untouched, with no refresh on its 401. Same-origin API requests also carry `ngsw-bypass: true`,
  which every Angular worker honours before matching a rule, so this build's own requests never reach an old
  worker. Raw `fetch` callers (catalog stream, offline snapshot, ping) use `withServiceWorkerBypassQuery` and
  `cache: 'no-store'`. The API answers every `/api` and `/hubs` response `Cache-Control: no-store`
  (`ProtectedResponseCacheMiddleware`, ahead of the error handler). Angular's worker ignores that header, so it
  protects the browser HTTP cache only.
- **Migration.** `ProtectedCacheSafetyService.start()` runs from the app initializer without blocking it.
  `ProtectedCacheMigrationService.ensureSafeVersion()` returns `safe | reloadRequired | blocked`:
  - It asks the registration's active worker the isolation query (`WORKER_QUERY_TIMEOUT_MS`). If no answer comes,
    it calls `registration.update()` and waits up to `WORKER_HANDOVER_TIMEOUT_MS` for the wrapper to activate.
  - It purges the caches `isLegacyProtectedCacheName` names (Angular 21's `ngsw:<scope>:<v>:data:<group>:cache`
    and `ngsw:<scope>:db:<v>:data:<group>:{age,lru}` for `api-fresh`, `ping-no-cache` and
    `offline-sales-api-no-cache`), emptying each one before deleting it.
  - It re-checks whenever another worker takes control (`controllerchange`) and asks for the current script
    again. An open page of this build undoes a mid-rollout flip to an old instance once that request reaches an
    instance of this build.
  - It purges again on every change of owner (`onDetach`, which a restart waits for). That keeps earlier answers
    off disk, but a page cannot stop a running old worker from replaying an answer it holds open. Only replacing
    that worker does.
  - App asset caches, other caches and IndexedDB are never touched, so queued invoices and drafts survive.
    Requests never wait on the state: `reloadRequired` and `blocked` only show the shell's `shell.cacheSafety.*`
    notice with a reload button. Development builds (worker disabled) leave any worker alone. Never advise
    clearing all site storage.
- **Coverage.** A tab still running an older build is protected once a page of this build loads anywhere in that
  browser, or the browser makes its next update check of `ngsw-worker.js` (any in-scope navigation). Until then,
  only that tab's own requests can be answered from its old caches.
- **Serving.** `src/server.ts` revalidates `index.html`, `ngsw.json`, the worker scripts and
  `manifest.webmanifest` on every load (`server-cache-policy.ts`); hashed bundles keep `max-age=1y`.
- **Rollback and kill switch.** Never deploy Angular's bare worker at `ngsw-worker.js` again, so never roll back
  below this change. The browser's update check would install it in every tab, and any bare worker re-reads the
  stored manifests of earlier versions (with their `/api` rules) for tabs still assigned to them. A rollback build
  must keep `build:worker`. Angular's kill switch still works at the same URL: serve `safety-worker.js` as
  `ngsw-worker.js`. It deletes every `ngsw:` cache, the app shell included, so offline billing cannot open while it
  is served. Queued sales in IndexedDB survive. Each page load registers it again, and it unregisters itself again.
- **Proof.** `bun run build && bun run audit:ui:release` (`tests/ui-audit/release/`) runs the built worker in
  Chromium against a legacy deployment: Angular's bare worker and the old configuration served over the current
  build files. No old app code runs; old-code requests are raw `fetch` calls without the bypass header. A control
  test reproduces the old leak. The other tests cover:
  - a slow cross-shop request;
  - a new user offline;
  - the first load of this build replacing the worker under an open old tab;
  - a lone old tab covered by the browser's update check;
  - an older page re-registering the URL mid-rollout (no script fetch);
  - a mid-rollout flip undone by the open app tab's own migration;
  - queued sales surviving the purge;
  - the shell opening offline while the API fails closed;
  - the production interceptor's headers.

## Quantity Input

- **One module.** `shared/quantity/` holds the units catalog (`quantity-units.ts`, mirrors
  `tests/contracts/quantity-units-v1.json`), the parse/format helpers (`quantity-input.ts`: locale-aware
  `parseQuantityInput`, `quantityStep`, `fitsQuantityScale`, `normalizeQuantity`) and
  `app-quantity-field`, used by the batch picker and the cart. A field shows the unit beside the input
  and its error under it with `role="alert"`; `1,5` parses in locales that use a decimal comma, while
  `NaN`, `Infinity`, `1e3`, empty, `0` and negatives are refused.
- **`setQuantity` never rounds.** `SaleCartStateService.setQuantity(key, qty)` sets an exact quantity. A
  value with more decimals than the line's `quantityScale` or above the batch stock is refused, the line
  keeps its previous quantity and the cart reports why. The plus and minus buttons step by
  `quantityStep(scale)` through the same method, with float sums normalized to 3 places.
- **Drafts carry the unit.** Cart lines and their IndexedDB drafts (keyed by user and shop) store `uom`,
  `quantityScale` and `documentUnit`. An offline snapshot row without `quantityScale` falls back to scale 0
  and `NOS`, so an old snapshot sells whole units only.
- **Errors.** Server answers are matched with the problem-code helper (`shared/utils/problem-code.util.ts`).
  `features/sales/utils/sale-quantity-error.ts` maps `Sale.` and `SaleReturn.QuantityPrecisionExceeded`;
  `features/inventory/state/inventory.effects.ts` maps `Item.UnitChangeWithHistory` and `Item.QuantityScale*`; the
  stock count and adjustment pages map `Inventory.QuantityPrecisionExceeded` to `inventory.quantityPrecisionExceeded`.
  A cart refusal for a too-precise quantity names the line's unit and allowed decimals (the server's own refusal
  does too when exactly one cart line is over its unit's precision; otherwise the generic text stays).
- **Stock count and adjustment.** `InventoryBatchDto` carries the item's `uom` and `quantityScale`. Both forms
  take up to 3 decimals, refuse an entry finer than the item's unit before submit
  (`shared/quantity/quantity-messages.ts` names the unit and the allowed decimals) and preview the result
  unrounded. An API that predates units sends neither field, and the storage ceiling of 3 applies.
- **Item form.** Add and edit item overlays pick a unit and a precision. A catalog unit fixes the
  precision; a custom name offers a 0 to 3 decimals select (changing back from a catalog label restores the
  custom precision the item had, whole numbers by default), and an item flagged for review shows a badge until
  the merchant saves it: a catalog-unit item is told that saving confirms the unit, a custom one to choose the
  precision.
- **Returns.** The return form allows `max(item scale, scale of the sold quantity)` decimals per line. A typed
  quantity above the returnable amount is kept and refused by the form's validation, never clamped.
- **Mocked UI proof.** The fractional-sale flow runs against mocked APIs in
  `tests/ui-audit/mock/new-sale-fractional-quantity.spec.ts`. Run it alone from `src/frontend` with
  `bun x playwright test --config playwright.config.ts tests/ui-audit/mock/new-sale-fractional-quantity.spec.ts`
  (`audit:ui:mocked` ignores a file argument).

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

## Recorded-cost Trading and Operating Figures

The P&L page and export use API economics version2: net sales excluding tax, COGS, independent
stock loss, gross trading profit, classified operating expenses/profit, gross margin and markup.
Zero is displayed as zero; null is translated as Not available with the operating-profit status.
Filtered activity suppresses whole-shop operating totals. Unclassified expense count/amount are
labeled period-wide and link to Expenses; service/return review and recorded-cost limitations stay
visible. Returned-goods wastage is labeled already included in COGS. Dashboard consumes the same
summary and calls its card gross trading profit; recorded-expense/outflow charts keep their cash
activity meaning. These pages do not describe audited or after-income-tax business profit.

Expense record/correction overlays require an explicit choice to assert Operating or Capital/other
excluded; the default is Unclassified. Supplier settlements show read-only source-provenance text.
Their corrections omit a new treatment and inherit the immutable backend treatment.

Successful checkout previews show net revenue, recorded cost and trading profit; rejected online
previews retain server net/cost/loss descriptions and clear earlier successful economics through
the existing request identity/shop epoch handling. Percentage ceilings are floored to two places.
Offline finalization refreshes usable snapshot costs and evaluates the unchanged v1 frozen output
with scaled bigint arithmetic before consuming an invoice number, saving a queue entry or reducing
shadow stock. Below-cost new issuance returns `BELOW_RECORDED_COST` with translated instructions
to reduce discount or finish online. V1 proportional allocation can breach one line where online
capped redistribution would succeed; this refusal never silently changes the frozen arithmetic.
Already-issued v1 invoices replay their original wire amounts/hash and post with a pricing review
case when below recorded cost; current-versus-frozen cost differences are provenance warnings.

Discount ceilings use the same scaled bigint quantity/cost midpoint rounding as offline issuance,
including remaining sale-level capacity and percentage flooring; binary `toFixed` products cannot
advertise an unsafe paise. The dashboard comparison falls back to a legacy growth value only when
the new field is absent. An explicitly null version2 comparison stays unavailable.

## Expense List Query and Refresh

Expenses keep one canonical `{search,status,page,pageSize}` query in NgRx, defaulting
to all/page 1/20. Search/status changes preserve other filters and page size, reset
to page 1, and request server-filtered rows. Pagination uses the matching server
count; rows retain server ordering. Each load has a fresh request ID, `switchMap`
cancels superseded loads, and reducers ignore success/failure from another ID.
The existing shop epoch/interceptor remains authoritative; scope reset clears the
query/results/ID and `scopeChanged` also cancels pending list work, including page
recovery. Filters remain usable while loading; old results and summaries are hidden.
A list failure stays separate from a successful empty result and mutation feedback.

Record/correction overlays refresh the current query after success. An out-of-range
response is never installed: a fresh request selects `max(1, ceil(totalCount/pageSize))`
with the same search/status/page size. Page 1 with no matches publishes normally.
Matching expenses counts the dataset; active amount, active/voided counts, and rows
explicitly describe this page in all nine locales. Active amount continues excluding
voided loaded rows and does not redefine accounting profit.


## Purchase Receiving Lots and Recovery

Both `/inventory/purchase-orders` and its detail route open the production receiving dialog.
A PO line may have several physical lot rows, each with a stable client UUID. Grouped remaining
quantity and duplicate item/trimmed batch validation apply across rows; PO quantities remain
whole units. Receipt history renders one row per immutable receipt line with batch navigation,
original quantity/cost and stock-movement source IDs. Batch labels remain current batch metadata.

`ReceivingDraftService` stores one IndexedDB record in `intelibill-purchase-receiving` keyed by
`[userId, shopId, purchaseOrderId]`. Editable fields and the exact pending request are claimed in
one transaction; dispatch waits for transaction completion. Storage failure prevents sending.
Revisions reject stale writers, and another tab cannot overwrite a pending/confirmed operation.
Rows bind directly to their FormGroup so replacing/rehydrating the array cannot leave rendered
inputs writing detached controls. PO refreshes update availability without rebuilding active input.

Definitive rejection preserves editable values and maps row errors back to stable client identity.
Sending restored after reload becomes unknown. Transport loss, 429/5xx, stale scope, payload
mismatch, lookup errors/misses and denied recovery remain frozen and retain the same operation ID,
client IDs and serialized payload. Check performs read-only operation lookup; Retry sends only the
saved request. A miss never permits a fresh operation. Confirmation checks key and target, writes
a confirmed tombstone plus immutable operation proof before cleanup, and refreshes live PO state
because the replay response may contain an older PO snapshot.

Receiving flushes on close/leave, detaches under its captured owner on scope transition, uses
`beginMutation()` and `REQUEST_SCOPE`, and correlates outcomes by owner + epoch + PO + operation.
An unrelated or late response cannot close or clear the active draft. Received and Closed orders
retain a Recover receipt entry point even when no quantity remains. Returning to the original
owner/shop restores pending work. Failed cleanup retains confirmed proof; it never resurrects an
editable receipt. Do not delete these records or operation receipts during rollback. Rollout order
is additive migration, replay-capable API, then web. Mobile and supplier-return UI are outside scope.

Release coverage uses the built Angular app, real IndexedDB and deterministic API fixtures in
`tests/ui-audit/release/purchase-receiving.spec.ts`. Fixture ledger counts establish UI behavior;
`PurchaseOrdersControllerTests` against real PostgreSQL establishes atomic posting, replay and
sale/cost-correction lineage. Build and run `bun run audit:ui:release` to exercise receiving and the
existing protected-cache-isolation release scenarios together.

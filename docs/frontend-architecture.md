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
| `SalesCartIndexedDbService` | `sales-cart` | Persists in-progress POS cart across page reloads |
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

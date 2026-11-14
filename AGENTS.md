# intelibill — AI inventory management system backend

## Build & Test

**Backend** (repo root, solution: `src/backend/Intelibill.slnx`):
- Build: `dotnet build src/backend/Intelibill.slnx`
- Run: `dotnet run --project src/backend/Intelibill.Api`
- Test full: `dotnet test src/backend/Intelibill.slnx`
- Test unit: `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests`, `Application.Unit.Tests`, `Api.Unit.Tests`
- Test integration: `dotnet test tests/backend/integration/Intelibill.Integration.Tests` (requires Docker)

**Migrations** (EF Core) — API startup does **not** apply them; run `database update` yourself after pulling schema changes (deployed environments use the migration job):
```bash
dotnet ef migrations add <MigrationName> \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
dotnet ef database update \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
```

**Frontend** (`src/frontend`, Bun only—no npm/yarn):
- Install: `bun install`
- Start: `bun run start`
- Build: `bun run build`
- Test: `bun run test`

**Mobile** (`src/mobile/android/intelibill_mobile`, Flutter Android only):
- Flutter: pin to `.flutter-version` (3.44.2); FVM optional (`fvm use` in mobile dir)
- Setup: `flutter pub get`
- Generate: `./tool/codegen.sh` (l10n + build_runner + format; commit `lib/**/*.g.dart`, `lib/**/*.freezed.dart`, `lib/src/core/localization/`)
- Analyze: `flutter analyze`
- Format: `dart format --set-exit-if-changed .`
- Test: `flutter test --coverage`
- Build: `flutter build apk --debug`

## Architecture

**Layer order**: Domain ← Application ← Infrastructure ← Api

| Layer | Deps | Purpose |
|---|---|---|
| **Domain** | None | Entities, value objects, domain interfaces |
| **Application** | Domain | Wolverine handlers, validators, errors (`Application/Common/Errors/Errors.cs`) |
| **Infrastructure** | Domain + App | EF Core DbContext, repos, configs in `Infrastructure/Data/Configurations` |
| **Api** | App + Infra | ASP.NET Core, controllers, middleware |

Dependency registration: each layer's `DependencyInjection.cs`

**Conventions**:
- ErrorOr result pattern (no exception-driven control flow)
- FluentValidation for requests
- Repository abstractions + `IUnitOfWork` for writes
- No data annotations on entities
- C# warnings = errors (analyzer-clean builds)
- EF: `IEntityTypeConfiguration<T>` for mappings
- Central Package Management (CPM): versions in `Directory.Packages.props` only, csproj entries versionless
- Angular: standalone components, NgRx root/feature stores, PrimeNG styling
- i18n: `src/frontend/public/assets/i18n` locale files
- Mobile: Feature-first clean architecture (domain-independent), Riverpod generated providers, GoRouter, Dio, Freezed/Json Serializable, flutter_secure_storage + shared_preferences, Very Good Analysis

## Multi-Shop Tenancy & Auth

- RLS-enforced row isolation per `active_shop_id`
- JWT carries `active_shop_id` claim; switching returns new scoped token
- Auth methods: verified email enrollment, verified phone enrollment and login, password reset, token refresh/revoke. No external identity provider: credentials are the only way in.
- Device sessions: every access token names an `AuthSession` (`sid`); only a credential check creates one, and a session can be revoked on its own. A token with no `sid` is refused.
- Refresh credentials: stored as SHA-256 only, single-use, chained per session (`parent_token_id`). Rotation and sign-out lock users → auth_sessions → refresh_tokens; a reused (consumed) credential ends its session. Web tabs serialize renewals via `RefreshCoordinator`. See `docs/backend-architecture.md` § Refresh Credentials.
- Shop memberships: roles = `Owner`, `Manager`, `Staff`
- Rate limiting: `RateLimitAttribute`/`RateLimitFilter` over `IAtomicRateLimitStore` (`rate_limit_buckets`, one atomic transaction across replicas); auth endpoints add account/destination budgets via `IRateLimitSubject` and answer 503 when the store is down. See `docs/backend-architecture.md` § Rate Limiting
- Logging: Serilog; every sink in both APIs sits behind `LogEventRedactor` (`Intelibill.Shared.Web`, `WriteToRedactedSinks`): exception objects become type/frame descriptions, credential names are masked at any depth (PII too with `EnablePiiMasking`). Never log an exception message, token, link or DTO by hand
- Errors: exceptions in both APIs answer `{type,title,status,code,traceId,errors?}` via the shared `SafeErrorFactory` (`Internal.Unexpected` for anything unplanned) and log through `SafeDiagnosticWriter`; `SafeSpanProcessor` strips exception text from exported spans. See `docs/backend-architecture.md` § Safe Errors and Diagnostics

## Environment

- .NET SDK: pinned via `global.json` (10.0.105, latestMinor roll-forward)
- Local DB: PostgreSQL, creds in `src/backend/Intelibill.Api/appsettings.Development.json` or `docker compose up -d`
- `appsettings.json`: intentionally empty (safe to commit)
- Production: env vars or secrets manager

DB Options: `src/backend/Intelibill.Infrastructure/Options/DatabaseOptions.cs:7`, Pattern in `"Database"` config section

## Tech Stack

| Layer | Tech |
|---|---|
| Backend | ASP.NET Core 10, C# latest, .NET 10.0.105 |
| Database | PostgreSQL + Npgsql + EF Core 10 |
| Messaging | Wolverine 5.24 (CQRS) |
| Validation | FluentValidation 12 |
| Errors | ErrorOr 2.0 |
| Tests | xUnit 2.9 + coverlet |
| Frontend | Angular 21 (`src/frontend/`) |
| Mobile | Flutter Android (`src/mobile/android/intelibill_mobile/`) |

## Current State (May 2026)

**Features**: Full inventory system (items, batches, sales, expenses, customers, suppliers, bank accounts)

**Key endpoints**:
- Auth: `POST register/email`, `login/email`, `password-reset/{request,confirm}`, `token/{refresh,revoke}`, `enrollment/email/{request,confirm}`, `enrollment/email/claim/{request,confirm}`, `reauthenticate`, `email-change/{request,confirm}`, `GET sign-in-methods`. `POST register/phone` is closed: it answers 503 `Auth.PhoneEnrollmentUnavailable`
- Phone (off unless `PhoneAuth:Enabled`): `GET phone/availability`, `POST phone/{enrollment,login}/{request,confirm}`, `POST phone/change/{request,confirm}` (authenticated)
- Sessions: `GET users/me/sessions`, `DELETE users/me/sessions/{id}`, `POST users/me/sessions/revoke-all` (ends every device, the caller's included)
- Shops: `GET me`, `{shopId}`, `POST` and `POST switch` (any active member; both return an access scope, never a refresh token), `PUT {shopId}`, `PUT {shopId}/tax-profile` (owner; regime, state, GSTIN, legal name, series, e-invoice attestation; `POST` takes an optional `taxProfile`), `PATCH {shopId}/members/{userId}`, `GET/POST {shopId}/invitations`, `DELETE {shopId}/invitations/{invitationId}`
- Items: `GET stream` (SSE), `GET`, `GET details`, `POST`, `PATCH {itemId}` (`POST`/`PATCH` take optional `quantityScale` 0-3; unit change after stock history → 409 `Item.UnitChangeWithHistory`)
- Inventory: `POST inbound`, `inbound/batch` (optional `operationKey`; per-row commit, resend replays), `GET batches` (each with its row `version`), `batches/available`, `PUT/POST/void batches/{batchId}`, `POST batches/{batchId}/count` (owner/manager; `expectedVersion` + reason, optional `operationKey`; stale version → 409 `Inventory.BatchVersionConflict`), `PUT batches/{batchId}/prices` (owner/manager; in place, required `expectedVersion`, appends a price revision), `POST receipts/{receiptValueId}/cost-corrections` (owner; posts only the delta, optional `operationKey`). Void refuses a consumed receipt (409 `Inventory.BatchConsumed`). Legacy `PUT batches/{batchId}` never replaces the batch: quantity/cost/supplier changes answer 400 `Inventory.Batch{Quantity,Cost,Supplier}EditMoved`
- Sales: `POST` (optional `placeOfSupplyStateCode`; refused until the shop declares a tax profile; due judged by the customer's credit policy, optional owner/manager `creditOverride {reason, observedExposure}`), `GET`, `GET {saleId}` (with the issued `document`, null for legacy sales), `GET profit-loss`
- Offline reconciliation (`api/offline-reconciliation`): `GET` (status filter, paging), `GET {caseId}` (detail adds `linkCandidates` for the viewer's link actions and the envelope's lease window), `POST {caseId}/resolve` (owners/managers take the policy's actions; staff may only `Escalate` their own open case; required `operationId` + `expectedVersion` + reason; stale → 409 `Reconciliation.CaseVersionConflict`), `POST recovery-submissions` (owner/manager; required `operationId`; checksummed bundle ≤100 invoices, ≤2 MiB; an envelope that already has an open case reuses it)
- Posting periods (`api/posting-periods`): `GET ?fiscalYear=` (owner/manager; 12 months, Apr..Mar), `POST {yyyy-MM}/close` and `{yyyy-MM}/reopen` (owner only; `reason` + required `operationKey`; closing a month that has not ended → 409 `PostingPeriod.NotEnded`; a posting dated in a closed month → 409 `PostingPeriod.Closed`, a contended fence → 409 `PostingPeriod.Busy`)
- Customers: CRUD (optional checksum-valid `gstin` + `stateCode`, `creditPolicyMode` NoCredit|Enforced|LegacyAdvisory + `creditTermsDays`, owner/manager only) + `account` (`?from=&to=`; saved `creditLimit`, policy, `availableCredit`, `openingBalance`/`closingBalance`, `overdueAmount`; sales carry issue-time `dueAmount`, `currentOutstandingAmount`, `dueDate`/`isOverdue`; `unallocatedCredit`, `receipts`) + `GET {id}/credit` (checkout preview, any member), `payments` (optional `operationKey`, `method`, `externalReference`, `bankAccountId`, `allocations`)
- Suppliers: CRUD + `ledger`, `payments` (optional `operationKey`, `method`, `externalReference`, `bankAccountId`, `allocations` to purchase receipts)
- Payments (`api/payments`): `GET {receiptId}`, `GET balances?partyType=&partyId=` (owner/manager), `POST {receiptId}/allocations` (owner/manager, optional `operationKey`), `POST {receiptId}/reversal` (owner; reason + required `operationKey`; whole receipt once → 409 `Payment.AlreadyReversed`)
- Financial operations: `GET financial-operations/{customer-payment|supplier-payment}/{key}` (owner/manager; committed outcome or 404)
- Expenses: `GET` (`search`, case-insensitive `status=all|active|voided`, page≥1, pageSize1–100; omitted status=all), `POST`, `{id}`, `{id}/correct`, `categories` (optional `treatment`: Unclassified default, Operating, CapitalOrOtherExcluded; supplier settlements are provenance-only and immutable through correction)
- Bank Accounts: CRUD
- Users: `GET` (shop), `PUT me` (returns the saved profile, no token), `me/change-password` (ends every device, including the caller's); `POST` (add) and `PUT {userId}` are refused — onboarding goes through shop invitations and shop administration through the membership endpoint
- Invitations: `POST invitations/{invitationId}/accept` (the invited person's own session)

**Infrastructure**:
- Profit economics: preview/posting protect tax-exclusive net revenue against rounded recorded batch cost (purchase-tax flags never imply recoverability). P&L/dashboard/XLSX add economics version2: net sales, COGS, independent stock loss, gross trading and classified operating profit, margin vs markup, explicit null/status/limitations. Voids reverse stock losses on their own dates; return wastage is already included in COGS. Legacy metric meanings remain. See `docs/backend-architecture.md` § Recorded-cost Profit and Discount Economics
- Verified phone: `phone_challenges` holds purpose-bound keyed-HMAC codes; `users.is_phone_verified` backfills false. Delivery via the encrypted `identity_sms` outbox and the `ISmsSender` seam. See `docs/auth-phone-setup.md` and `docs/backend-architecture.md` § Verified Phone Credentials
- Realtime: hub connections are admitted by `RealtimeConnectionGate` against live authority, recorded in `realtime_connection_leases`, addressed per payload by `IAuthorizedRealtimeDispatcher<THub>` (no SignalR groups) and closed by `RealtimeRevocationWorker`'s five-second sweep. See `docs/backend-architecture.md` § Realtime Notifications
- Financial operations: keyed payments run through `IFinancialOperationExecutor`, which reserves an `operation_receipts` row (unique per shop + type + key) and commits it with the posting; retries replay the stored response, changed payloads answer 409 `Operation.PayloadMismatch`. Bulk stock intake commits a manifest receipt plus one receipt per row through it (`IInboundRowExecutor`, fresh scope per row). See `docs/backend-architecture.md` § Financial Operation Receipts and § Bulk Stock Intake
- Migration: `20260425075353_InitialCreate` (single consolidated)
- Customer credit: `CustomerCreditPolicy` decides new due under the customer row lock taken first among business locks in `RecordSale` (after the period fence) (`LockForCreditAsync`, `FOR NO KEY UPDATE`, 5 s → 409 `Customer.CreditBusy`); existing customers migrate to visibly unenforced `LegacyAdvisory`; overdue needs a due date (issue date + terms, never invented); offline violations post with a `CustomerVariance` review case. See `docs/backend-architecture.md` § Customer Credit Policy
- Payment receipts: every customer/supplier payment posts an immutable `PaymentReceipt` with its ledger entry (+ supplier expense) and optional `PaymentAllocation`s under party → target locks (`FOR NO KEY UPDATE`, 5 s → 409 `Payment.Busy`); `IInvoiceBalanceReader` derives invoice / purchase-receipt outstanding and unallocated credit; reversals (`PaymentReversal`, `PAYMENT_REVERSAL` / `REVERSAL` ledger entry, expense voided) stop the receipt's allocations. Return due cap = min(account balance, invoice outstanding). Legacy payments backfill as `UNKNOWN_RECORDED_METHOD` with no allocation (`scripts/db/backfill-payment-receipts.sql`). See `docs/backend-architecture.md` § Payment Receipts and Allocations
- Sale returns: record and void run in `ISaleReturnMutationCoordinator` under the customer + sale row locks (`FOR NO KEY UPDATE`, 5 s budget → 409 `SaleReturn.SaleBusy`); lock order (period fence →) customer → sale → credit notes → batches; `CreditNote.HasBeenRedeemed` checks balance too. See `docs/backend-architecture.md` § Sale Return Serialization
- Stock corrections: `inventory_batches`/`inventory` use `xmin` as row version; every quantity change writes one signed movement in the same save, every count a `stock_counts` row. See `docs/backend-architecture.md` § Stock Corrections and Voids
- Purchase receipts: every receipt writes a `purchase_receipt_values` row (original + effective total, `xmin` version); cost corrections post a signed `COST_ADJUSTED` ledger entry plus a `purchase_receipt_cost_adjustments` row, never touching batch cost or sale COGS; legacy receipts without clear evidence are `LEGACY_UNRESOLVED` and refuse correction. See `docs/backend-architecture.md` § Purchase Receipts, Price Revisions and Cost Corrections
- Return accounting: every return posts a snapshot in its own save (per-line taxable/tax/non-tax split by `IReturnAccountingPolicy` `proportional-line-tax/1`, `sale_return_settlements` legs: receivable reduction, store credit issued, external refund); exports and the P&L read `ReturnAdjustmentReader` (return on its processing date, void as a reversal on its void date); Tally refuses (409) returns needing review and any unbalanced voucher. Legacy returns: `scripts/db/backfill-sale-return-accounting.sql`. See `docs/backend-architecture.md` § Return Accounting Exports
- Offline invoice arithmetic: `OfflineInvoiceArithmeticValidator` recomputes every frozen offline invoice under its `calculationVersion` (absent = legacy float replay, 1 = integer paise/half-away/largest-remainder, pinned by `tests/contracts/offline-invoice-arithmetic-v1.json`) before anything posts; a mismatch answers NeedsReview with `reviewCaseId` + field `mismatches` (`ArithmeticConflict` reconciliation issue). Posted-sale audit: `scripts/db/audit-offline-sale-arithmetic.sql`. See `docs/backend-architecture.md` § Offline Invoice Arithmetic
- Issued documents: `InvoiceDocumentBuilder` applies the capability matrix (`docs/gst-invoice-capability-matrix.md`: Regular → tax invoice / bill of supply / invoice-cum-bill, Composition → bill of supply, Unregistered → receipt; undeclared profile, e-invoice-required, composition/unregistered tax and composition interstate refused) and freezes an `IssuedInvoiceSnapshot` v2 in `sales.invoice_document` (jsonb; seller/buyer, place of supply, HSN, CGST/SGST/UTGST/IGST by `component-split/1`). `IInvoiceNumberAllocator` numbers online sales inside the posting transaction and reserves offline ranges from the same `FOR UPDATE` row of `invoice_sequences` (unique per shop + fiscal year + series and per seller registration + fiscal year + series), format `A/2627/000001` (≤16 chars). Fiscal years follow `BusinessCalendar` (IST). Tally posts component ledgers. See `docs/backend-architecture.md` § Issued Invoice Documents and Fiscal Numbering
- Offline reconciliation: sync stores an `OfflineSubmission` envelope first, judges the historical lease on first receipt (5-minute skew) and files one case per held envelope (adopting an open pre-envelope case for the same hold); cases resolve through `ReconciliationActionPolicy` actions (executor types 7/8, row lock + `xmin`; a late invoice for a closed posting period is held as `ClosedPeriodReview` and replays via `RetryHistoricalLease` after reopen); owners/managers import checksummed recovery bundles that always open a review (timing-checked like sync). Invoice leases expire at `min(reserved + 7 days, 1 April 00:00 IST)`, never across a fiscal year, and freeze the seller profile their sales are issued under. Web console at `/sales/offline-reconciliation`. See `docs/backend-architecture.md` § Offline Reconciliation
- Offline printing: finalization freezes the queued sale's `document` (v2) on the device from the lease's frozen seller profile (`InvoiceLeaseDto.sellerProfile`, also on snapshot `activeLease`) and snapshot customer GSTIN/state/address (snapshot schema 3) via `buildOfflineInvoiceDocument`, pinned to the server builder by `tests/contracts/offline-invoice-document-v2.json`; `offline=1` prints pass `offlineInvoicePrintGuard` + `OfflineInvoiceAccessService` (own shop/device/issuer, offline grace when the API is unreachable). See `docs/frontend-architecture.md` § Offline Invoice Printing
- Shop context: every session write passes `ShopContextService.transition` (`core/context`); a change of user/shop bumps the tab epoch, detaches drafts under their owner, resets NgRx feature slices and hides routed content, then restarts at `/` between two signed-in scopes (sign-out goes to `/login` instead). Switch and shop creation wait for `prepareToLeave()`. `authInterceptor` drops API answers from a left epoch (`StaleScopeError` for `REQUEST_SCOPE` writes: checkout, intake, offline sync) and refuses owner-bound writes under another scope. Cart drafts are keyed by user + shop (IndexedDB v2). See `docs/frontend-architecture.md` § Shop Context Transitions
- Protected API cache isolation: `bun run build` publishes `service-worker/protected-api-worker.js` as `ngsw-worker.js` (the URL every build registered), with Angular's worker beside it as `ngsw-angular-worker.js` (`build:worker`). Its fetch listener runs first and leaves same-origin `/api`/`/hubs` requests to the network, so an open tab on an older build is covered once this build loads in that browser or the browser re-checks the worker. `ngsw-config.json` has no data groups. `authInterceptor` sends the bearer token only to `API_BASE_URL`, plus `ngsw-bypass` on same-origin API calls. The API answers `/api`/`/hubs` with `Cache-Control: no-store` (`ProtectedResponseCacheMiddleware`). `ProtectedCacheMigrationService` replaces an earlier worker via `registration.update()` (isolation-query check, re-run on `controllerchange`) and purges only the legacy `api-fresh`/`ping-no-cache`/`offline-sales-api-no-cache` caches, never IndexedDB. Never ship Angular's bare worker at `ngsw-worker.js` again. Release proof: `bun run build && bun run audit:ui:release`. See `docs/frontend-architecture.md` § Protected API Cache Isolation
- Shared styles: tokens (`--app-*`) on `:root` in `styles.css`; repeated families live once as `ib-` classes in the UI kit (`shared/styles/ui-kit/`), delivered by `UiKitStylesComponent` (shell's first child, lazy shell chunk, never on `/login`) and `AuthKitStylesComponent` (auth pages); layer order `theme, base, primeng, properties, components, utilities, ib-base, ib-kit`, then unlayered component rules (always win). Signed-in-only globals in `ShellGlobalStylesComponent`. `bun run audit:styles:duplicates` ceiling 27 (only goes down). See `docs/frontend-architecture.md` § Shared Styles
- Offline pack: `build:worker` keeps `ngsw.json` as the full pack (all JS/CSS + fonts, locales, icons prefetched) and writes `ngsw-lite.json` (no asset groups, network navigations), both with `appData.buildId`; the wrapper answers Angular's `ngsw.json` fetch with the lite one only when Cache Storage `intelibill:offline-pack:state` says `off` (missing = full, so pre-flag devices keep their pack). `OfflinePackUpdateService` (initializer, lazy, never awaited) writes `on` iff any shop on the device is enabled, then checks for updates at start/30 min (visible + API reachable)/`online`/visible; a new build is only offered (shell "Update now" → `prepareToLeave()` → activate + reload), a same-build pack switch is activated at once; enabled devices re-verify the pack and refresh snapshots older than 6 h. Enabling adds step `appPack` before the snapshot (`OfflinePackService.prepare`: free space, `persist()`, flag on, wait, verify every prefetched URL in `ngsw:<scope>:<hash>:assets:*` + non-degraded `/ngsw/state`; else `APP_PACK_INCOMPLETE`, flag released unless another shop needs it); skipped without a service worker. Status in `intelibill.offlineSales.offlinePack.v1`, shown in Manage shop. Release proof `tests/ui-audit/release/offline-pack.spec.ts`. See `docs/frontend-architecture.md` § Offline Pack
- RLS: session interceptor sets `app.current_user_id` + `app.active_shop_id`
- Business dates and posting periods: `shops.business_time_zone` (Asia/Kolkata, read-only in the API); sales, returns (+ `voided_business_date`) store `business_date` + zone, reports/exports read those columns, instant-only tables use `IBusinessClock.GetUtcWindow` (`UtcDateWindow`, half-open); fiscal year of a sale's number from its business date (lease reservation and offline timing still use the IST `FiscalYear.ForDate`; identical while IST is the only zone). Monthly `PostingPeriod`s (rows created on first touch, Owner-only close/reopen, `PostingPeriodEvent` audit, executor types 11/12); `IPostingPeriodPolicy` fences the postings listed in the architecture doc before any business row lock (`FOR SHARE`, 5 s budget restoring the caller's `lock_timeout`; Closed/Busy 409); lock order: operation receipt → (reconciliation case row lock, for replays) → period fence → till/session when enabled → customer/supplier → sale/receipt → credit notes → batches. Not fenced: payment allocations, credit-note void, reconciliation `LinkStockCorrection` catch-up movement. Adjustment void, supplier payment reversal and supplier reassignment are refused when the original's month is closed. A late offline invoice for a closed month is held as `ClosedPeriodReview` (never redated or renumbered; `RetryHistoricalLease` after reopen). Rollout triggers date old pods' inserts; `scripts/db/backfill-business-dates.sql`. See `docs/backend-architecture.md` § Business Dates and Posting Periods
- Quantity precision: the item owns `UnitCode`/`QuantityScale`/`UnitReviewRequired`; `QuantityPolicy` (max 3 decimals) is checked by sale, sync, return, intake, count and adjustment, which answer `*.QuantityPrecisionExceeded`; documents print the item's UQC. Run `scripts/db/backfill-item-quantity-precision.sql` once after rollout. See `docs/backend-architecture.md` § Quantity Precision
- Shop authority: `ShopMembership.Status`/`AuthorityVersion`; role and status changes, invitation issue and acceptance all run under one authority-lock order (users → shop → membership/invitation rows), and the token's shop scope is re-checked against the live membership on every request
- Tests: Testcontainers (real PostgreSQL via Docker)

## Domain Model

**Entities**: `User`, `AuthSession`, `RealtimeConnectionLease`, `PhoneChallenge`, `IdentitySms`, `Shop`, `ShopMembership`, `ShopInvitation`, `Item`, `Inventory`, `InventoryBatch`, `StockTransaction`, `Sale`, `SaleItem`, `Customer`, `CustomerLedgerEntry`, `Supplier`, `SupplierLedgerEntry`, `Expense`, `ExpenseCategory`, `BankAccount`, `RefreshToken`, `PasswordResetToken`, `OperationReceipt`, `StockCount`, `PurchaseReceiptValue`, `PurchaseReceiptCostAdjustment`, `BatchPriceRevision`, `SaleReturnSettlement`, `OfflineSubmission`, `ReconciliationIssue`, `ReconciliationCaseEvent`, `InvoiceSequence`, `InvoiceLease`, `PaymentReceipt`, `PaymentAllocation`, `PaymentReversal`, `PostingPeriod`, `PostingPeriodEvent`

**Value objects**: `IssuedInvoiceSnapshot` (+ `InvoiceSellerProfile`), `FiscalYear`, `QuantityPolicy`, `QuantityUnits`, `ItemStockHistory`, `BusinessDay`, `UtcDateWindow`

**Enums**: `ShopRole`, `MembershipStatus`, `CredentialKind`, `PaymentMethod`, `BankAccountType`, `StockTransactionType`, `SupplierLedgerEntryType`, `CustomerLedgerEntryType`, `SupplierStatus`, `FinancialOperationType`, `OperationReceiptStatus`, `PurchaseReceiptSource`, `PurchaseReceiptValueStatus`, `ReturnAccountingStatus`, `ReturnSettlementKind`, `ReconciliationCaseStatus`, `ReconciliationAction`, `OfflineSubmissionSource`, `TaxRegime`, `EInvoiceStatus`, `InvoiceDocumentType`, `TaxComponentKind`, `PaymentPartyType`, `ReceiptTenderMethod`, `PaymentAllocationTargetType`, `CreditPolicyMode`, `PostingPeriodState`, `PostingPeriodAction`

## Structure

| Path | Purpose |
|---|---|
| `src/backend/Intelibill.Domain/` | Entities, value objects, domain interfaces (zero deps) |
| `src/backend/Intelibill.Application/` | Handlers, validators (depends: Domain only) |
| `src/backend/Intelibill.Infrastructure/` | DbContext, repos, configs (depends: Domain + Application) |
| `src/backend/Intelibill.Api/` | Controllers, middleware (depends: Application + Infrastructure) |
| `tests/backend/unit/` | Domain + Application unit tests |
| `tests/backend/integration/` | Integration tests vs API |
| `Directory.Build.props` | Shared MSBuild (nullable, warnings-as-errors, analysis, CPM) |
| `Directory.Packages.props` | Central Package Management (all NuGet versions) |
| `global.json` | SDK pin (10.0.105, latestMinor roll-forward) |

## NuGet Packages

Add version to `Directory.Packages.props`:
```xml
<PackageVersion Include="Pkg.Name" Version="X.Y.Z" />
```
Add to `.csproj` (no version):
```xml
<PackageReference Include="Pkg.Name" />
```

## CodeGraph

`.codegraph/` is the repository's code knowledge graph.

- Use `codegraph_context` first for broad codebase tasks.
- Use `codegraph_search` and `codegraph_node` for symbol lookup.
- Use `codegraph_callers` and `codegraph_callees` to trace execution.
- Use `codegraph_impact` before changing shared symbols.
- Use `rg`, `rg --files`, and direct file reads for configuration, documentation,
  and other non-symbol searches.
- If `.codegraph/` is missing, ask before running `codegraph init -i`.

## Links (Reference—Don't Duplicate)

- README.md
- AGENTS.md (this file)
- docs/architectural_patterns.md
- docs/backend-architecture.md
- docs/frontend-architecture.md
- docs/mobile-architecture.md
- Directory.Build.props
- Directory.Packages.props
- global.json

## Supplier Returns and Receipt Reversal

- Migration: `20261107180954_AddSupplierReturnsAndReceiptReversals`; apply before API/web. Preserve schema and facts on rollback.
- Routes: `GET purchase-receipts/{id}`, `POST supplier-returns/preview`, `POST/GET supplier-returns`, `GET supplier-returns/{id}`, `POST supplier-returns/{id}/credit`, `POST purchase-receipts/{id}/reversal`, `POST purchase-order-receipts/{id}/reversal`.
- Owner/Manager: read and ship pending source-linked returns. Owner only: accepted credit and receipt/group reversal. New mutations require operation key, versions and reason; authorization precedes replay.
- Pending shipment changes stock only. Accepted credit reduces net payable; paid receipts retain allocations and expose surplus supplier credit without inventing a refund. After shipment, cost correction/reassignment/void are blocked. Full reversal rejects consumption/restoration/counts/returns/active allocations and reverses the exact effective total. Historical PO receiving and procurement status remain unchanged.
- Operations14/15/16; ledger supplier return credit6; stock supplier return9. Never classify supplier shipment as sales, COGS or independent stock loss. No legal GST note, return cancellation/credit reversal, automatic refund/PO reopening or mobile workflow.
- Web receipt-lot/return routes share durable owner/action/target drafts; preserve unknown payloads across404/reload/tab/shop changes. Printed references use frozen facts and the commercial-not-GST disclaimer.

Supplier return print rollout also requires `20261107190743_AddSupplierReturnLotSnapshots` before API/web. It adds nullable frozen lot numbers without backfilling live names into historical facts. New shipments capture their original lot number; older return references truthfully fall back to the persisted receipt-value ID.

## Sale Cancellations

- Rollout: `docs/sale-cancellation-rollout.md`; migration `20261107201224_AddSaleCancellations` precedes API/web. All API pods/readers must understand cancellation before an approved pilot is allowlisted. `SaleCancellation:Enabled=false` and `AllowedShopIds=[]` are the defaults; both enabled and exact shop membership are required for a new posting. No live approval is implied.
- Policy `receipt-cash-due/1`: Owner/Manager, same open shop-local month, online v2 Unregistered zero-tax Receipt, all original goods recovered; explicit operator-confirmed cash refund plus exact original due credit. Refuse external paid tenders, applied/redeemed credit, return history, collection ambiguity, offline, services and tax documents. No partial cancellation, automatic transfer, uncancel or mobile workflow.
- Routes: `POST sales/{id}/cancellation-preview`, `POST sales/{id}/cancellations`, `GET financial-operations/sale-cancellation/{key}`. New writes require key, nonzero expected `xmin` version, reason and confirmations. Authorize before replay; lock operation → period → customer → sale → batches/items, then reload. Original invoice/snapshot/amounts remain immutable; cancellation is separate terminal status/facts.
- Operation 17, customer ledger 6 (`SALE_CANCELLATION_CREDIT`), stock 10 (`SALE_CANCELLATION`). Reports reverse frozen original revenue/recorded COGS on cancellation date, separately from returns/stock loss; Tally uses a balanced commercial Journal. A4/thermal keep original receipt facts plus linked cancellation disclaimer.
- Web freezes owner/shop/sale payload in IndexedDB before HTTP, uses revision claims and confirmation tombstones; unknown outcomes/lookup404 never permit a new key. Disable writes to roll back; retain facts/schema/readers/guards/replay. Pre-feature binaries and migration Down are unsafe after posting. Commercial cancellation record is not a GST credit note; cash refund is the operator's confirmation.


## Till Sessions and Counted Close

- Rollout: `docs/till-sessions-rollout.md`. Apply ordered migrations `20261108110138_AddTillSessions`, `20261108113929_AddSaleTillEvidence`, `20261108120219_AddPaymentExpenseSettlementEvidence`, `20261108122550_AddReturnTillSettlementEvidence`, `20261108130102_AddTillReconciliationEvidence` before API/readers/web and every source writer. `TillSessions:Enabled=false`, `AllowedShopIds=[]`, `MixedTenderEnabled=false`; mixed tender unavailable. No pilot enablement is implied. All five Down methods refuse rollback; retain schema/facts/readers/replay, never deploy old writers after facts.
- Routes: `GET /api/tills/capabilities`, `GET/POST /api/tills`, `GET /api/tills/sessions`, `GET /api/tills/sessions/{id}`, `POST /api/tills/{id}/sessions`, `POST /api/tills/sessions/{id}/{cash-movements|close|review}`, `GET /api/tills/operations/{create|open|cash-movement|close|review}/{key}`. Staff own sessions/receipts; managers shop-wide, physical till creation/review Owner/Manager. Explicit opening/count fields (zero valid), keys and mutation versions required. Frozen close/variance/review; handover close/open, no cross-day trade/reopen. Live manager may finalize inactive cashier with reason; source/open active-cashier checks remain. Overdue finalization requires manager before replay and original open period.
- Period→till→party→document→credit→stock; `ITillPostingService` freshly acquires within each source-owned transaction, stages only, never reuse after commit/rollback. All-due attribution advances xmin. Source matrix in rollout: sales/collections positive, supplier payments/refunds/independent expenses negative, supplier-linked expense never double-counted, corrections/reversals/return void only explicit actual settlement. Known tender evidence is manually recorded, not verified gateway/bank settlement. Cancellation eligibility unchanged. Operations18–22 till,23–24 expense,25–26 return/void; saved return identity `{saleReturnId,saleId}`, existing public return POST current SaleDto/void204 retained.
- Offline shared poster enforces frozen `Till`, `TenderLines`, `TillBusinessDate`, `TillCashierId`; invalid context held `TillReview=13`. Owner/Manager `ReconcileTillSale=6` appends immutable excluded late supplement without changing original invoice/date/envelope/count. Complete four-field verified-issuer TillReview may ordinary RetryHistoricalLease through same gate/period/till checks; legacy/partial cannot disabled-fallthrough. Staff only eligible own Escalate before replay. Recorded legacy evidence matches original Cash/UPI/Card; positive-paid legacy Credit may use separately reasoned tender or Unknown, never rewrite envelope.
- Web `/tills` and `/tills/sessions/:id/print`; new till-aware source selectors/durable IndexedDB action claims freeze owner/shop/payload/key before HTTP, preserve unknown404/reload/tab/scope outcomes and outer payment key/proof. Released pre-Till localStorage K1 recovery works unchanged with IDB unavailable; never decorate its historical payload with current till. Payment reversal recovers by exact same-key endpoint retry, no generic lookup slug. Entry epoch guards include A→B→A and cached completion. Parent financial links use API projections, withheld for Staff. No mobile/mixed tender/gateway automation/bank reconciliation. Verify backend full solution + EF pending model; frontend `bun run test --watch=false`, `bun run build`, then `bun run audit:ui:release`; preserve budgets/warnings.

## Expiry-aware Selling and Quarantine

- Rollout: `docs/expiry-aware-selling-rollout.md`; ordered migrations `20261108182442_AddExpiryPolicies`, `20261108184604_AddExpirySaleEvidence`, `20261108193354_AddOfflineExpiryEvidence`, `20261108210054_AddBatchDispositionEvidence` precede every API/read/write path and web. All Down methods refuse; retain facts/schema/compatible readers/writers. No pre-feature writers after facts; no production enablement implied. Nonregulated API/web goods only, mobile excluded.
- Default Ignore/optional expiry/sell-through/7 warning days/no override. Owner reasoned CAS edits at `GET/PUT /api/expiry-policies[/items/{id}]`; null item policy inherits. Owner/Manager `GET /api/expiry-policies/readiness`; read-only explicit-shop/date audit `scripts/db/audit-expiry-readiness.sql` matches counts, invents no dates/quantities. Unknown history is not Ignore.
- Lock order operation/reconciliation → period → expiry (`FOR SHARE` posting, `FOR UPDATE` policy/date) → till → party → document/credit → batch/item. Never expiry after batch;5s `Expiry.PolicyBusy` restores caller timeout. Required missing date always hard-blocks; Block has no override. Warn approval requires live Owner/Manager, exact summed batch quantity/xmin/policy/catalog/reason. Date edits require xmin/reason and append immutable history; no quarantine release by date/policy edit.
- Schema4 server evidence persists before stream, lasts7days, validates historical issue/lease/issuer/revisions. No offline warning approval. Stale/forged/partial/unknown authority holds ExpiryReview14; legacy only proven migrated pre-feature Ignore. Retain envelopes/hashes, never decorate with current facts; only evidence-backed retry/link/till actions, no post-anyway. Disconnected remote policy edits cannot be learned instantly.
- Physical quantity includes quarantine. Restockable expired (including Ignore)/required-undated recovery creates immutable source roots; exact child balances equal batch hold. `GET/POST /api/inventory/batches/{id}/dispositions`; Owner Release/Dispose with key/xmin/root/scale/reason, operation27 and `/api/financial-operations/batch-disposition/{key}` lookup authorize before replay. Release never revives originally expired roots; missing-date release requires audited correction/current eligibility. Dispose recognizes independent recorded-cost loss once, no generic void. Quarantine is no impairment/write-off; COGS reversal remains once. Counts, adjustment create/void, supplier shipment/receipt reversal and source void preserve root conservation. Durable web unknown keys/payloads remain scoped and frozen.

## Owner Till Setup

- Migration `20261109171054_AddOwnerTillSettings` precedes the owner-settings API. Update all source writers before exposing setup; pre-settings binaries cannot be a rollback after owner changes.
- Manage shop → Tills & day close is active-shop scoped. Owners configure; Owner/Manager read readiness. GET/PUT `shops/{shopId}/till-settings`; PUT requires explicit enabled, observed version (zero valid), reason and confirmation. Release `TillSessions.Enabled` plus exact allowlist remains necessary; new shops opt in, existing till users are preserved by migration.
- Shared shop settings gate spans every source transaction, including disabled/no-till paths; changes/opening take exclusive catalog. Disable refuses open/overdue sessions, awaiting reviews and unresolved offline evidence; owner also confirms every device queue was checked. Retain history/audit/replay and immutable change events. Unknown PUT + old-version GET stays uncertain; exact retry/newer read recovers. Browser setting events invalidate verified capability and selection across tabs.

## Supporting Services (`src/services/`, `tests/services/`)

- `src/services/barcode-master-api` (`BarcodeAPI.sln`, own `AGENTS.md`, tests in `tests/services/barcode-master-api`, docs in `docs/services/barcode-master-api`) and `src/services/hsn-finder-ai` (`Sankra.HsnFinder.sln`, no test project yet) were imported from their standalone repositories with history (`git subtree add`, then moved). They are separate deployables, not part of `src/backend/Intelibill.slnx`.
- `Directory.Build.props` + `Directory.Packages.props` in `src/services/` and `tests/services/` stop the root build settings and Central Package Management from applying: each service keeps versions in its own csproj files. Keep the two copies identical; do not add service packages to the root `Directory.Packages.props`.
- Build/test: `dotnet test src/services/barcode-master-api/BarcodeAPI.sln`, `dotnet build src/services/hsn-finder-ai/Sankra.HsnFinder.sln`. Each service has its own pipeline: `.github/workflows/barcode-master-api-ci.yml`, `.github/workflows/hsn-finder-ai-ci.yml` (path-filtered to that service).
- Run: `dotnet run --project src/services/barcode-master-api/BarcodeAPI.Api` (http 5209, needs its Postgres `DefaultConnection`), `dotnet run --project src/services/hsn-finder-ai/Sankra.HsnFinder.Api` (https 5001, matches `HsnService:BaseUrl` in the API's development settings). Local `appsettings.Development.json` / `appsettings.secrets.json` are git-ignored.
- Unmerged work from the old repositories is kept as tags `archive/barcode-master-api/cs-branch-1`, `archive/hsn-finder-ai/cs-branch-1`, `archive/hsn-finder-ai/chans-temp-placeholder` (original root-relative paths; superseded implementations, not merged).

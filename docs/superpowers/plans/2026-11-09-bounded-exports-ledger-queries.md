# Bounded exports and ledger queries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for each assigned Sol implementation task. Steps use checkbox (`- [ ]`) syntax. The user has authorized automatic implementation, review/fixes and PR creation; no new approval pause is required.

**Goal:** Bound synchronous exports and first-party party directory/search reads while preserving accounting output and old array API clients.

**Architecture:** A scoped read store owns a repeatable PostgreSQL export snapshot, scalar preflight and bounded keyset pages. SQL ledger/directory aggregates replace materialized histories. New directory and ID endpoints support remote tables/selectors while existing routes stay compatible.

**Tech Stack:** ASP.NET Core 10, EF Core/Npgsql/PostgreSQL, ErrorOr, Wolverine, Angular/NgRx/PrimeNG, Bun, existing xUnit/Testcontainers and Vitest.

**Spec:** `docs/superpowers/specs/2026-11-09-bounded-exports-ledger-queries-design.md` (read completely before implementing).

## Global Constraints

- PDF 2,000 logical rows; XLSX/XML 50,000 logical rows; source-record budget 50,000; every materializing database page at most 1,000. Directory default20/max100, page default1, search maximum200, debounce250ms.
- Preserve Domain ← Application ← Infrastructure ← Api; no EF dependency in Application. No schema/index changes without representative EXPLAIN evidence.
- Preserve business dates, independently dated return/void/cancellation facts, financial signs, RLS, live authorization, offline snapshots, till/expiry facts and durable mutation intents.
- Legacy arrays stay at existing routes. New first-party online callers use bounded routes only. Global summary cards must retain complete-shop semantics.
- Use Bun, keep existing responsive/accessibility patterns and all nine locale key sets aligned. No deployment or feature enablement.

## Review Focus

- An old sale outside the window has an in-window return/void/cancellation, or a huge original invoice: include the right adjustment, enforce output and source bounds (Tasks1/4).
- A return is voided or a sale cancelled between count and page reads: all pages and status labels stay in the one snapshot (Tasks1/4).
- Signed supplier corrections and cancellation credits coexist with positive/negative party accounts: SQL totals match exact current arithmetic (Tasks2/4).
- Same-name/late-page parties, selected draft IDs, and A→B→A while search is pending: preserve correct party and shop, never use a matching name as identity (Tasks2/3).
- Directory filtering and an edit removing the last visible row: server-filtered count/page and full-shop summary remain correct; offline selection makes no directory requests (Tasks2/3).

---

## Task 1: Snapshot-backed export preflight and bounded dataset reads

**Modify:**
- `src/backend/Intelibill.Application/Features/Exports/Sales/Queries/ExportSales/ExportSalesQueryHandler.cs`
- `src/backend/Intelibill.Application/Features/Exports/Sales/Services/ISalesExportDatasetBuilder.cs`
- `src/backend/Intelibill.Application/Features/Exports/Sales/Services/SalesExportDatasetBuilder.cs`
- `src/backend/Intelibill.Application/Features/Exports/Accounting/ReturnAdjustmentProjector.cs` (only deterministic tie-break/extracted pure projection if needed)
- `src/backend/Intelibill.Application/Common/Errors/Errors.Export.cs`
- `src/backend/Intelibill.Infrastructure/DependencyInjection.cs`
- Existing sales renderers in `src/backend/Intelibill.Infrastructure/Services/Exports/` only for cancellation checks or shared counting contracts; keep report content.

**Create:**
- `src/backend/Intelibill.Application/Features/Exports/Sales/Services/IExportReadStore.cs`
- `src/backend/Intelibill.Application/Features/Exports/Sales/Services/ExportReadModels.cs`
- `src/backend/Intelibill.Application/Features/Exports/Sales/Services/ExportReadLimits.cs`
- `src/backend/Intelibill.Infrastructure/Services/Exports/ExportReadStore.cs` (split into `.Counts.cs` and `.Pages.cs` partials if otherwise unwieldy)
- `tests/backend/integration/Intelibill.Integration.Tests/ExportQueryBudgetTests.cs`

**Update tests:** `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Exports/Sales/Queries/ExportSales/ExportSalesQueryHandlerTests.cs`, `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Exports/Sales/SalesExportDatasetBuilderTests.cs`, `tests/backend/integration/Intelibill.Integration.Tests/SalesExportsControllerTests.cs` and `ReturnAccountingExportTests.cs`.

**Interfaces:**
- Application seam `Task<IExportReadSnapshot> OpenSnapshotAsync(CancellationToken ct)`; `IExportReadSnapshot : IAsyncDisposable` exposes `Task<ExportCounts> CountAsync(Guid shopId, ExportWindow window, string level, string format, CancellationToken ct)` and `Task<ExportPage> ReadPageAsync(Guid shopId, ExportWindow window, string level, ExportCursor? cursor, int pageSize, CancellationToken ct)`.
- `ExportWindow(DateOnly From, DateOnly To)`; `ExportCounts(long OutputRows, long SourceRows)`; cursor identifies stream, timestamp, document ID and optional child ID. `ExportPage` has `SummaryRows`, `LineItemRows`, `TaxBreakup`, `ReturnAdjustments`, `CancellationAdjustments` using the existing DTO element types, `ExportCursor? NextCursor`, and `bool IsComplete`. Each source stream emits facts once; the builder merges tax buckets and attaches period adjustments to sale summaries after reading all streams. Use existing pure projection helpers to retain financial arithmetic. The store may make a fixed number of child-page queries per source page, but no per-sale/per-return repository calls. Nested source collections also use capped queries.
- Change the builder contract to `Task<SalesExportDatasetDto> BuildAsync(IExportReadSnapshot snapshot, Shop shop, User generatedBy, DateOnly startDate, DateOnly endDate, string level, CancellationToken ct)`. The handler opens the snapshot, counts and rejects over-budget work before invoking this method, then disposes the snapshot before renderer invocation. The builder loops `ReadPageAsync` with page size1,000, merges its existing DTO collections and generates metadata. Do not retain the old unbounded builder overload as a fallback.
- Extract pure row mapping from the current builder/projectors only as required; infrastructure projects data and must not invent new financial calculations. Retain no-tracking reads and existing frozen invoice facts.

- [ ] Add failing unit tests `OversizedPdf_DoesNotLoadDataset` (2,001), `PdfBoundary_2000Succeeds`, `LineItemCap_CountsLines`, `OversizedXlsxAndXml_DoNotRender` (50,001), `CancelledBeforeRender_DoesNotRender`. Assert no detail/page call for preflight rejection, not merely no renderer call.
- [ ] Add initial PostgreSQL regressions: adjustment-only period counts; two dated postings from one return; large original sale outside the window exceeding source budget; cancellation-only detail rows; SQL source counts not loading entity graphs. Add exact limit tests using small fake counts for handler and real rows for at least PDF boundary/line count.
- [ ] Run `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter FullyQualifiedName~Features.Exports.Sales` and the new integration filter; record the expected failures before implementation.
- [ ] Implement the snapshot seam with `BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)` and read-only transaction configuration, explicit shop predicates and no tracking. Own/dispose its transaction; refuse an incompatible ambient transaction. Build count predicates once and reuse them in pages. Count using SQL `LongCount`/SUM and overflow-safe arithmetic with the exact renderer-specific formula in the spec.
- [ ] Replace whole-range sale reads and per-sale physical-return calls with capped keyset source/child reads and grouped return quantities. Keep preflight source coverage of all referenced histories. Process independently dated streams even with no in-window sales; preserve original sale statuses/references, tax breakup, return settlements and frozen cancellation cost/revenue. End snapshot before render, propagate cancellation into every command and page loop.
- [ ] Run the targeted tests plus existing renderer/accounting regression tests. Inspect generated XLSX/XML small fixtures and PDF meaningful values rather than unstable byte equality.
- [ ] Commit the cohesive deliverable: `feat: bound sales export reads and snapshots`.

## Task 2: SQL ledger aggregation and compatible paged party APIs

**Modify:**
- `src/backend/Intelibill.Domain/Interfaces/Repositories/ICustomerRepository.cs`, `ISupplierRepository.cs`, `ICustomerLedgerEntryRepository.cs`, `ISupplierLedgerEntryRepository.cs`
- `src/backend/Intelibill.Infrastructure/Repositories/CustomerRepository.cs`, `SupplierRepository.cs`, `CustomerLedgerEntryRepository.cs`, `SupplierLedgerEntryRepository.cs`
- `src/backend/Intelibill.Application/Features/Customers/Queries/GetCustomers/GetCustomersQueryHandler.cs`
- `src/backend/Intelibill.Application/Features/Suppliers/Queries/GetSuppliers/GetSuppliersQueryHandler.cs`
- `src/backend/Intelibill.Api/Controllers/CustomersController.cs`, `SuppliersController.cs`
- `src/backend/Intelibill.Infrastructure/DependencyInjection.cs` and Application DI only if explicit registrations are required.

**Create:**
- `src/backend/Intelibill.Domain/ReadModels/PartyDirectoryQuery.cs` (query/page/summary read models; use existing folder naming conventions if a matching type already exists)
- `src/backend/Intelibill.Application/Features/Customers/Queries/GetCustomerDirectory/GetCustomerDirectoryQuery.cs`, `GetCustomerDirectoryQueryHandler.cs`, `GetCustomerDirectoryQueryValidator.cs`
- Equivalent three files under `Features/Suppliers/Queries/GetSupplierDirectory/`
- Customer/supplier by-ID query/handler files under `Features/Customers/Queries/GetCustomer/` and `Features/Suppliers/Queries/GetSupplier/`
- `tests/backend/integration/Intelibill.Integration.Tests/DirectoryQueryBudgetTests.cs`, `LedgerAggregateTests.cs`

**Interfaces:**
- `PartyDirectoryQuery(string? Search, string Status = "all", int PageNumber = 1, int PageSize = 20)`; paged result has `Items`, `TotalCount`, `PageNumber`, `PageSize`. Typed customer/supplier result adds the spec's typed `Summary`.
- Repositories: `GetDirectoryAsync(Guid shopId, PartyDirectoryQuery query, CancellationToken ct)` for customers; supplier adds `bool includeSystem` before ct. Expose scoped summaries as dedicated query methods/read store if needed; they must remain SQL aggregates.
- Add `GetSupplierBalancesAsync(Guid shopId, IReadOnlyCollection<Guid> supplierIds, CancellationToken ct)` returning `IReadOnlyDictionary<Guid,decimal>` and use it in both legacy and paged handlers. Existing customer grouped method keeps its signature with SQL implementation.
- New routes and exact query/response behavior are defined in the spec. Reuse existing `CustomerDto`/`SupplierDto`. Shared DTO mapper may be extracted to avoid drift between legacy, paged and single-ID handlers.

- [ ] Add `LedgerBalances_AllEntryTypesAgree`: customer +100,-40,-10,+5=55, then payment reversal and cancellation credit; supplier goods/payment/positive and negative corrections/signed reversal/signed return credit; empty accounts; positive and negative party nets; no other-shop contribution. Assert all current customer enum values are represented, and unknown values do not silently acquire a sign.
- [ ] Add `LegacyDirectory_RetainsArrayShape`, `DirectoryFilter_AppliesBeforePaging`, `SameNameParties_StablePagination`, `SupplierPage_ConstantQueryCount`, `DirectorySummary_IsGlobalNotFirstPage`, `DirectoryBounds_AreValidated`, `LiteralWildcards_AreNotPatterns`, `PartyById_IsShopScoped`, and shop-local month-boundary/cancellation revenue regressions. Seed matching records beyond page1; test include_system=false and displayed Unassigned search.
- [ ] Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter 'FullyQualifiedName~DirectoryQueryBudgetTests|FullyQualifiedName~LedgerAggregateTests'`; record failing expectations before implementation.
- [ ] Replace customer/supplier materialized ledger sums with translatable SUM/CASE/GROUP BY queries. Preserve unknown-customer-type rejection, signed supplier amounts, empty=0 and per-party positive-net totals. Add the grouped supplier method; use bounded returned IDs for page enrichment.
- [ ] Implement filter→count→stable order→skip/take plus summaries. Query validation rejects invalid bounds rather than clamping silently. Avoid integer offset overflow. SQL summaries ignore search/status and obey visibility rules. Preserve existing customer clock/metric semantics, including cancellation subtraction.
- [ ] Add directory and ID query/controller wiring with same current-member authorization and scoped404 behavior; retain the existing array route payloads. API path `{id:guid}` must not shadow `/directory`.
- [ ] Run the targeted integration suite, related customer/supplier/payment unit tests and backend build. Commit `feat: add bounded party directories and ledger aggregates`.

## Task 3: Remote directories and online party selectors

**Modify:**
- `src/frontend/src/app/core/auth/auth.constants.ts`
- `src/frontend/src/app/features/customers/services/customer.service.ts`, `features/suppliers/services/supplier.service.ts`
- Both feature `state/{customers,suppliers}.{actions,effects,reducer,selectors,facade}.ts` and their existing specs
- `features/customers/pages/customers-page.component.{ts,html}` and `components/customers-table.component.{ts,html}` (filter bar if required)
- `features/suppliers/pages/suppliers-page.component.{ts,html}` and `components/suppliers-table.component.{ts,html}`
- `features/sales/pages/new-sale-page.{state,lifecycle,search-customer}.service.ts`, `new-sale-page.component.{ts,html}` and `features/sales/components/new-sale/sale-customer-section.component.{ts,html}` if external search/loading input is needed
- `features/inventory/services/batch-row-form-state.service.ts`, `features/inventory/pages/inventory-batch-page.component.ts`, `features/inventory/components/batch-page/batch-row-form.component.{ts,html}`
- `features/purchase-orders/pages/purchase-order-builder-page.component.ts`
- `src/frontend/public/assets/i18n/{en-IN,hi-IN,bn-IN,gu-IN,kn-IN,ml-IN,mr-IN,ta-IN,te-IN}.json`
- Corresponding existing `.spec.ts` files; add selector-service/component tests where missing.

**Create:** `src/frontend/src/app/shared/models/party-directory.models.ts`; a shared component-scoped remote search helper only if it reduces duplication without sharing selected values between callers.

**Interfaces:**
- Service `getDirectory(query: PartyDirectoryQuery): Observable<CustomerDirectoryPage>` and supplier equivalent with `includeSystem`; `getCustomer(id)` / `getSupplier(id)` for exact restoration. Keep old array service methods for compatibility tests, but no live online consumer uses them.
- NgRx request carries query plus request identity; success carries page/summary and matching identity. Reset with shop context, discard obsolete requests. Expose `totalCount`, query/page and typed global summary. Selected parties live separately from transient page/search results.
- Selector events/results carry ID-bearing DTOs. Display disambiguating phone/city/ID text for duplicate names; keep existing free-text behavior only where it is already supported.

- [ ] Write failing HTTP/effect/page tests for query serialization, search outside page1, server pagination, status reset to page1, full-shop cards with a one-row page, latest-search wins, last-row edit correcting page, and mutation refreshing summary.
- [ ] Add POS/intake/PO tests selecting a late-page duplicate-name party by ID, preserving selected party when search changes, restoring a route/draft ID by the ID endpoint, rejecting old search results after A→B→A, and making **zero** directory/ID HTTP calls during offline POS search. Preserve frozen draft payloads/keys and existing customer credit behavior.
- [ ] Run focused frontend specs with `bun run test --watch=false` and the runner's supported include/filter option (inspect `src/frontend/package.json` and existing test scripts before choosing syntax); record expected failures.
- [ ] Implement service models/endpoints; switch directory effects/state/pages/tables to remote page/count/summary. Remove local full-directory filtering/slicing and card reductions. Debounce search250ms, cancel stale searches and retain scope guards. After mutations reload, then correct an out-of-range page once.
- [ ] Migrate all online selector callers listed above to their own bounded search data, not global directory page state. Remove eager full-directory loads from new-sale/intake/PO lifecycle. Resolve IDs directly, keep selected values stable, and disambiguate duplicate labels. Preserve offline snapshot search as a separate branch without online fallback.
- [ ] Add localized size-limit/search/paging/loading/error text as needed in all nine locales, using existing accessible controls and feedback. Search source for `getCustomers(` / `getSuppliers(` and facade full-array loads; prove no remaining live online path exhausts the directory. Compatibility methods/tests and explicit offline snapshot readers are allowed.
- [ ] Run touched specs and locale/accessibility coverage; build once before commit. Commit `feat: page party directories and search selectors remotely`.

## Task 4: Adversarial query budgets, snapshot verification and release evidence

**Modify/create:** `tests/backend/integration/Intelibill.Integration.Tests/ExportQueryBudgetTests.cs`, `DirectoryQueryBudgetTests.cs`, `LedgerAggregateTests.cs`; create `tests/backend/integration/Intelibill.Integration.Tests/ReadQueryProbe.cs` (test-only DbCommandInterceptor/reader counters and synchronization barriers); update `docs/backend-architecture.md`, `docs/frontend-architecture.md`; create `docs/bounded-reads-verification.md`.

**Interfaces:** Reuse `ApiWebApplicationFactory(configureTestServices: ...)`, its existing `IInterceptor` registration and restricted runtime role. The probe attributes commands to the request/store operation, excludes migrations/auth/session setup, records query count and actual rows read, and supports a barrier after count or first page. Never log SQL parameter values containing party/customer data.

- [ ] Add `Export_QueryCountScalesByPage` for 100 and1,000 sales with returns; assert commands bounded by a documented fixed base + fixed queries per capped source/child page, not one call per sale. Record actual formula and query counts after implementation; a threshold high enough to admit N+1 is invalid.
- [ ] Add `SameTimestampPages_NoDuplicates`, `ConcurrentInsert_DoesNotShiftSnapshot`, `ConcurrentReturnVoidAndCancellation_StayInSnapshot`, `CanceledExport_StopsFurtherPages`, `OtherShopRows_NeverAppear`, `OversizedExport_NoDetailReader`, and `AllMaterializedReads_AreCapped`. Concurrent tests use a second connection and deterministic barriers, not timing sleeps; verify a later fresh export sees the newly committed facts.
- [ ] Seed10,000 customers/suppliers with a late-page match and multiple ledger rows. Assert returned items≤100, complete filtered count, exact global summaries, constant commands per page and no entity-history reads. A balance query can scan ledger rows in PostgreSQL but returns only aggregates. Use runtime-role API requests to prove RLS and explicit filters.
- [ ] Run targeted budget/accounting integration tests and record observed commands, rows, elapsed time and allocation/peak-memory observations for representative and near-limit synthetic exports. Run EXPLAIN on the actual generated expensive SQL only if measurement suggests a query/index issue; add indexes only with a documented plan improvement and write-cost tradeoff.
- [ ] Run final backend full `dotnet test src/backend/Intelibill.slnx`; frontend `bun run test --watch=false`, `bun run build`, `bun run audit:ui:release` from `src/frontend`. Do not repeat green full suites without new changes. If shared return projection changed, ensure dashboard/P&L regression suites are included. Check `dotnet ef migrations has-pending-model-changes --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api` when feasible; no schema change is expected.
- [ ] Update architecture and verification docs with new routes, exact limits/count semantics, snapshot lifetime, compatibility limitation, measured results and release/rollback behavior. Do not claim production capacity from local measurements.
- [ ] Commit `test: verify bounded reads under concurrent trading` and hand off for the requested independent thorough review. Fix findings and rerun affected checks before PR creation.

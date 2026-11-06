# Business timezone and posting periods — implementation plan (backlog 27)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transaction dates, reporting ranges and accounting exports agree on the shop's business date, and a closed posting period cannot receive a new posting.

**Architecture:** Instants stay UTC. Every sale and return freezes the business date (and zone) it was posted on; reports filter on that stored date, so a later zone change moves nothing. One `IBusinessClock` turns instants into business dates and business-date ranges into half-open UTC windows. Monthly `posting_periods` rows are fenced with `FOR SHARE` inside each posting transaction (first business lock), and closed/reopened under `FOR UPDATE` on the same row.

**Tech Stack:** ASP.NET Core 10, C#, ErrorOr, FluentValidation, Wolverine, EF Core 10 / Npgsql, PostgreSQL, xUnit + Testcontainers.

**Spec:** `pending-tasks/27-business-timezone-and-posting-periods-done.md` (backlog 27, market-readiness review 2026-10-21). Re-verified against `main` at `2148ec1a`.

## Analysis: what the code does today

| Area | Finding |
|---|---|
| `Domain/Common/BusinessCalendar.cs` | Fixed IST (+05:30) `DateOf(instant)` / `StartOf(date)`. Used by `FiscalYear`, `Sale.ApplyCreditTerms` (due date), `InvoiceDocumentBuilder`, `TallyVoucherBuilder`, `ReversePaymentCommandHandler`, customer account `asOf`. Plan 20 already moved fiscal numbering onto it. |
| `Shop` | No time zone. |
| `Sale` / `SaleReturn` | Only instants (`SoldAt`, `ProcessedAt`, `VoidedAt`); no stored business date. |
| Repositories | `SaleRepository`, `SaleReturnRepository`, `InventoryAdjustmentRepository`, `PurchaseOrderRepository` turn `DateOnly` into **UTC** midnight (`ToUtcStart`). A 02:00 IST sale on 21 Oct (20:30Z on 20 Oct) lands in 20 Oct. Daily trend groups by `SoldAt.UtcDateTime`. |
| Ledger / number dates | `DateOnly.FromDateTime(x.UtcDateTime)` in `RecordSaleCommandHandler:301`, `OfflineSalePoster:172`, `RecordSaleReturnCommandHandler:177`, `VoidSaleReturnCommandHandler:146`, `CorrectReceiptCostCommandHandler:133`, `AddInventoryCommandHandler:179`, `VoidBatchCommandHandler:127`, `BatchFactory:107`. `RET-`/`ADJ-`/`CN-` prefixes use the UTC date. |
| "Today" defaults | UTC in `GetSalesQueryHandler:33`, `GetProfitLossReportQueryHandler:32`, `GetDashboardQueryHandler:33`, `ExportsController:34,65`, `SaleReturnValidator:72`, `GetCustomersQueryHandler:21`. |
| Exports | `SalesExportDatasetBuilder` already reads returns through `ReturnAdjustmentReader` by their own posting dates (plan 17), but `ReturnAdjustmentProjector.InRange` and `GetPostedInRangeAsync` use UTC dates. Tally already uses IST via `BusinessCalendar`. Excel/PDF renderers print `SaleDate.UtcDateTime`. |
| Posting periods | None. Nothing stops a backdated expense/payment or a late offline invoice into a month the accountant has finished. |
| Time source | `TimeProvider` is a registered singleton (`Infrastructure/DependencyInjection.cs:179`); most posting handlers still call `DateTimeOffset.UtcNow`. |

## Decisions

1. **One India zone at launch.** `Shop.BusinessTimeZoneId` (`varchar(64)`, NOT NULL, default `Asia/Kolkata`). No API changes it in this release (GET shop returns it read-only). Any future change is prospective by construction: posted facts carry their own business date.
2. **Stored business dates.** `sales.business_date` + `sales.business_time_zone`; `sale_returns.business_date` + `business_time_zone` + nullable `voided_business_date`. Customer/supplier ledger entry dates (`DateOnly`) are computed from the same business date. User-entered `DateOnly` payment/expense dates are already business dates and are never reinterpreted.
3. **Report windows.** Sale/return report queries filter the stored `business_date` columns (inclusive `DateOnly` range). Instant-only tables (inventory adjustments, purchase orders, sales for customer metrics) use `IBusinessClock.GetUtcWindow` → `[StartInclusive, EndExclusive)`. No repository adds a fixed offset.
4. **Fiscal year** = `FiscalYear.ForBusinessDate(businessDate)` everywhere a sale is posted (same calendar as plan 20; no second year calculation).
5. **Posting periods are calendar months** of the business calendar: `PostingPeriod(ShopId, StartDate, EndDate, State Open|Closed)`, unique `(shop_id, start_date)`. Missing rows are created on demand (`INSERT … ON CONFLICT DO NOTHING`) by both the posting fence and close/reopen, so a missing period cannot bypass the fence.
6. **Authority:** only an **Owner** closes or reopens; both need a reason (1–500 chars) and an `operationKey` (executor types `PostingPeriodClose = 11`, `PostingPeriodReopen = 12`). Owners and managers may list periods. A period can be closed only after it has ended (`EndDate < today` in the shop zone) → else 409 `PostingPeriod.NotEnded`. Every close/reopen appends a `posting_period_events` row (action, reason, actor, time).
7. **Fence:** `IPostingPeriodPolicy.AuthorizeForWriteAsync` requires an open transaction, takes `FOR SHARE` on the period row and returns 409 `PostingPeriod.Closed` when closed. It is the **first business lock** (after operation-receipt reservation, before customer/supplier/sale/credit-note/batch locks). Close/reopen takes `FOR UPDATE` on the same row, then rechecks the actor's live Owner role and the state.
8. **Corrections post in the current period.** A return or void of a sale from a closed month posts on today's business date and is allowed (it references the original sale). A backdated expense/payment into a closed month is refused. Expense correction is refused when either the original's or the new date's period is closed.
9. **Late offline invoices** keep their issued time and business date. If that period is closed the invoice is held as `ReconciliationIssueType.ClosedPeriodReview = 12` with code `PostingPeriod.Closed` — never redated, never renumbered. `RetryHistoricalLease` is offered for it; the replay re-runs the fence, so it succeeds only after the owner reopens the period.
10. **Out of scope (recorded):** till sessions (plan 33 does not exist yet, so `TillCloseAndPeriodClose_DoNotDeadlock` is deferred to plan 33); web UI for period close (API only in this PR; follow-up); mobile.

## Global Constraints

- Domain ← Application ← Infrastructure ← Api; repository abstractions; ErrorOr (no exception control flow); `IUnitOfWork` for writes; FluentValidation for requests.
- Warnings are errors; package versions only in `Directory.Packages.props` (no new package is expected).
- Tenant and actor from authenticated context; new tenant tables get `ENABLE` + `FORCE ROW LEVEL SECURITY` with the `app.active_shop_id` predicate (copy `20261106120920_AddPaymentReceiptsAndAllocations.cs:188-207`), an entry in `Infrastructure/Data/TenantPolicyManifest.cs` and a row in `docs/security/tenant-policy-matrix.md`.
- The deployed migrator is not BYPASSRLS: any backfill loops shop by shop setting `app.active_shop_id` (copy the `DO $backfill$` block in the same migration) and is repeated as an idempotent `scripts/db/` script.
- Decimal money, immutable posted facts, explicit reversals, audit reasons.
- No migration rewrites an existing column; new columns only. `dotnet ef migrations add` with `--project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api`.
- Integration tests run against real PostgreSQL (Testcontainers; Docker required). Wrap the integration project in `timeout 1800`.
- Commit messages: Conventional Commits; **no AI attribution lines** (user rule).

## Review Focus

1. A sale at `2026-10-20T20:30:00Z` (02:00 IST, 21 Oct) appears only in 21 Oct totals, ledger date and Tally voucher date → Task 3 `IstEarlyMorning_BelongsToLocalDay`.
2. `2026-03-31T18:29:59Z` vs `2026-03-31T18:30:00Z` (23:59:59 / 00:00 IST, 1 April) fall in different days and fiscal series `2526` vs `2627` → Task 3 `FiscalYearBoundary_SplitsAtIstMidnight`.
3. A posting racing a period close never commits into a period after the close committed → Task 4 race tests.
4. A late offline invoice dated in a closed month is held, not redated or renumbered → Task 6 `LateOfflineInvoice_RequiresExplicitResolution`.
5. Rows written by old pods during the rolling deploy (no business date) still get one → Task 1 trigger + `BusinessDateUpgradeTests`.

---

### Task 1: Business clock, shop zone and stored business dates (schema)

**Files:**
- Modify: `src/backend/Intelibill.Domain/Common/BusinessCalendar.cs` — add zone overloads
- Create: `src/backend/Intelibill.Domain/ValueObjects/UtcDateWindow.cs`, `src/backend/Intelibill.Domain/ValueObjects/BusinessDay.cs`
- Create: `src/backend/Intelibill.Domain/Common/BusinessTimeZones.cs`
- Create: `src/backend/Intelibill.Application/Common/Time/IBusinessClock.cs`
- Create: `src/backend/Intelibill.Infrastructure/Services/Time/BusinessClock.cs` (+ register in `Infrastructure/DependencyInjection.cs`)
- Modify: `Shop.cs`, `Sale.cs`, `SaleReturn.cs`, their `*Configuration.cs`, shop DTO(s) returned by `GET api/shops/{shopId}` and `GET api/shops/me`
- Create: migration `AddBusinessDatesAndShopTimeZone`, `scripts/db/backfill-business-dates.sql`
- Test: `tests/backend/unit/Intelibill.Domain.Unit.Tests/Common/BusinessCalendarTests.cs`, `tests/backend/unit/Intelibill.Application.Unit.Tests/Common/Time/BusinessClockTests.cs` (or Api.Unit.Tests if Infrastructure types are only visible there — follow where existing Infrastructure service tests live), `tests/backend/integration/Intelibill.Integration.Tests/BusinessDateUpgradeTests.cs`

**Interfaces:**
- Produces:
  - `BusinessTimeZones.DefaultId = "Asia/Kolkata"`; `static TimeZoneInfo Resolve(string zoneId)` (cached `TimeZoneInfo.FindSystemTimeZoneById`).
  - `BusinessCalendar.DateOf(DateTimeOffset instant, TimeZoneInfo zone) : DateOnly`; `BusinessCalendar.StartOf(DateOnly date, TimeZoneInfo zone) : DateTimeOffset` (UTC). Existing IST overloads stay.
  - `public readonly record struct UtcDateWindow(DateTimeOffset StartInclusive, DateTimeOffset EndExclusive)` (`Contains(DateTimeOffset)` helper).
  - `public readonly record struct BusinessDay(DateOnly Date, string TimeZoneId)`.
  - `IBusinessClock`: `DateTimeOffset UtcNow { get; }`; `DateOnly GetBusinessDate(DateTimeOffset instant, string timeZoneId)`; `BusinessDay GetBusinessDay(DateTimeOffset instant, string timeZoneId)`; `DateOnly Today(string timeZoneId)`; `UtcDateWindow GetUtcWindow(DateOnly fromInclusive, DateOnly toInclusive, string timeZoneId)` (end = start of `toInclusive + 1`). Implemented over the registered `TimeProvider`.
  - `Shop.BusinessTimeZoneId : string` (set to `BusinessTimeZones.DefaultId` in `Shop.Create`).
  - `Sale.BusinessDate : DateOnly`, `Sale.BusinessTimeZoneId : string` — set by a new required `BusinessDay businessDay` parameter on every `Sale` factory (`Sale.Record` and the offline factory), placed right after `soldAt`.
  - `SaleReturn.BusinessDate`, `SaleReturn.BusinessTimeZoneId` (new `BusinessDay` parameter on its factory, after `processedAt`), `SaleReturn.VoidedBusinessDate : DateOnly?` — `Void(DateTimeOffset voidedAt, DateOnly voidedBusinessDate, Guid voidedBy, string reason)`.
  - DB columns: `shops.business_time_zone varchar(64) NOT NULL DEFAULT 'Asia/Kolkata'`; `sales.business_date date NOT NULL`, `sales.business_time_zone varchar(64) NOT NULL`; `sale_returns.business_date date NOT NULL`, `sale_returns.business_time_zone varchar(64) NOT NULL`, `sale_returns.voided_business_date date NULL`; indexes `ix_sales_shop_id_business_date`, `ix_sale_returns_shop_id_business_date`, `ix_sale_returns_shop_id_voided_business_date`.

- [ ] **Step 1: Write failing unit tests** — `BusinessCalendarTests`: `DateOf(2026-10-20T20:30:00Z, Asia/Kolkata) == 2026-10-21`; `DateOf(2026-10-20T18:29:59Z) == 2026-10-20`, `DateOf(2026-10-20T18:30:00Z) == 2026-10-21`; `StartOf(2026-04-01, Asia/Kolkata) == 2026-03-31T18:30:00Z`. `BusinessClockTests` with a fixed `TimeProvider`: `Today` at `2026-10-20T20:30Z` is `2026-10-21`; `GetUtcWindow(2026-10-21, 2026-10-21)` is `[2026-10-20T18:30Z, 2026-10-21T18:30Z)`; `GetUtcWindow(2026-03-01, 2026-03-31)` ends at `2026-03-31T18:30Z`.
- [ ] **Step 2: Run, verify red** — `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter FullyQualifiedName~BusinessCalendar` → compile failure / FAIL.
- [ ] **Step 3: Implement** the domain types, `IBusinessClock`/`BusinessClock` (singleton), entity properties and factory parameters. Fix every caller of the changed factories so the solution builds: callers that already know the shop pass `clock.GetBusinessDay(instant, shop.BusinessTimeZoneId)`; elsewhere (tests, seeders) pass `new BusinessDay(BusinessCalendar.DateOf(instant), BusinessTimeZones.DefaultId)`. (Task 2 makes every production caller use the clock.) Map columns in `ShopConfiguration`, `SaleConfiguration`, `SaleReturnConfiguration` (snake_case like neighbours).
- [ ] **Step 4: Migration.** Generate `AddBusinessDatesAndShopTimeZone`, then hand-edit `Up` so that: columns are added **nullable**; a `DO $backfill$` loop per shop (setting `app.active_shop_id`) sets `business_time_zone = 'Asia/Kolkata'`, `business_date = (sold_at AT TIME ZONE 'Asia/Kolkata')::date` on sales, `(processed_at AT TIME ZONE 'Asia/Kolkata')::date` on returns, `voided_business_date = (voided_at AT TIME ZONE 'Asia/Kolkata')::date` where `voided_at IS NOT NULL`; then `SET NOT NULL` on the non-nullable columns. Add a rolling-deploy guard: `BEFORE INSERT OR UPDATE` triggers `sales_business_date_default` / `sale_returns_business_date_default` that fill a NULL `business_time_zone` with `'Asia/Kolkata'` and a NULL `business_date` / `voided_business_date` (when `voided_at` is set) from the instant in that zone — so an old pod's insert still gets a date. `Down` drops triggers, functions, indexes, columns. Write `scripts/db/backfill-business-dates.sql` with the same idempotent per-shop statements (only rows still NULL).
- [ ] **Step 5: Upgrade test** — `BusinessDateUpgradeTests.LegacyInsertWithoutBusinessDate_GetsIstDate`: with the real schema, insert a `sales` row via raw SQL without `business_date`/`business_time_zone` at `sold_at = '2026-10-20T20:30:00Z'`; read it back → `business_date = 2026-10-21`, zone `Asia/Kolkata`. Same for `sale_returns` including a void update.
- [ ] **Step 6: Verify** — `dotnet build src/backend/Intelibill.slnx` (0 warnings); `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests`; the Application/Api unit projects; `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter "FullyQualifiedName~BusinessDateUpgradeTests|FullyQualifiedName~TenantPolicyManifestTests"` → PASS.
- [ ] **Step 7: Commit** `feat(time): store business dates and shop time zone`.

### Task 2: Post with business dates

**Files (modify):** `RecordSaleCommandHandler.cs`, `OfflineSalePoster.cs`, `SyncOfflineSalesCommandHandler.cs`, `RecordSaleReturnCommandHandler.cs`, `VoidSaleReturnCommandHandler.cs`, `SaleReturnMutationCoordinator.cs` (if it owns the time), `SaleReturnNumberGenerator.cs`, `InventoryAdjustmentNumberGenerator.cs`, `CreditNoteCodeGenerator.cs`, `CorrectReceiptCostCommandHandler.cs`, `AddInventoryCommandHandler.cs`, `VoidBatchCommandHandler.cs`, `BatchFactory.cs`, `ReversePaymentCommandHandler.cs`, `SaleReturnValidator.cs`, plus the matching unit tests.

**Interfaces:**
- Consumes: `IBusinessClock`, `BusinessDay`, `Shop.BusinessTimeZoneId` (Task 1).
- Produces: number generators take `DateOnly businessDate` instead of a timestamp for the date part: `SaleReturnNumberGenerator.Generate(DateOnly businessDate)`, `InventoryAdjustmentNumberGenerator.Generate(DateOnly businessDate)`, `CreditNoteCodeGenerator` equivalent (keep the rest of each format unchanged: `RET-yyyyMMdd-XXXXXXXX`).

- [ ] **Step 1: Failing unit tests** — in `RecordSaleCommandHandlerTests`: with clock at `2026-10-20T20:30:00Z`, the recorded sale has `BusinessDate == 2026-10-21`, its `SaleDue` ledger entry date is `2026-10-21`, and the allocated series uses `FiscalYear(2026)`; with clock at `2026-03-31T18:30:00Z` the series fiscal year is `2026` and at `2026-03-31T18:29:59Z` it is `2025`. Return handler test: return processed at `2026-10-20T20:30Z` → `BusinessDate 2026-10-21`, return number starts `RET-20261021-`, ledger date `2026-10-21`. Offline poster test: claimed `SoldAt 2026-10-20T20:30Z` → sale and ledger business date `2026-10-21`.
- [ ] **Step 2: Run, verify red** — `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter "FullyQualifiedName~RecordSaleCommandHandlerTests|FullyQualifiedName~RecordSaleReturn|FullyQualifiedName~SyncOfflineSales"`.
- [ ] **Step 3: Implement** — inject `IBusinessClock`; posting time = `clock.UtcNow` (not `DateTimeOffset.UtcNow`) in every handler listed; business day from `clock.GetBusinessDay(instant, shop.BusinessTimeZoneId)` (load the shop through `IShopRepository.GetByIdAsync` where the handler does not have it); `FiscalYear.ForBusinessDate(businessDay.Date)` in `RecordSaleCommandHandler`; every `DateOnly.FromDateTime(x.UtcDateTime)` listed in the Analysis table becomes the business date; the "today" in `SaleReturnValidator` and `ReversePaymentCommandHandler` becomes `clock.Today(zone)`. Offline sales: business day of the **claimed** `SoldAt` (issued time), never of the sync time.
- [ ] **Step 4: Verify** — all three unit projects PASS; `grep -rn "UtcDateTime" src/backend/Intelibill.Application` shows no remaining date derivation for postings (display-only uses may remain).
- [ ] **Step 5: Commit** `feat(time): post sales, returns and ledgers on business dates`.

### Task 3: Read on business dates (reports, exports, dashboard)

**Files (modify):** `SaleRepository.cs`, `SaleReturnRepository.cs`, `InventoryAdjustmentRepository.cs`, `PurchaseOrderRepository.cs` and their interfaces in `Domain/Interfaces/Repositories`; `GetSalesQueryHandler.cs`, `GetProfitLossReportQueryHandler.cs`, `ProfitLossReportBuilder.cs`, `GetDashboardQueryHandler.cs`, `GetCustomersQueryHandler.cs`, `ExportsController.cs` (or the query it calls), `ReturnAdjustmentReader.cs`, `ReturnAdjustmentProjector.cs` (+ `ReturnAdjustmentEntry`), `SalesExportDatasetBuilder.cs`, export row DTOs, `SalesExcelSummaryRenderer.cs`, `SalesExcelLineItemRenderer.cs`, `SalesExcelReturnAdjustmentsRenderer.cs`, `SalesPdfDocument.cs`, `ProfitLossExcelExportRenderer.cs`, `TallyVoucherBuilder.cs`.
**Create:** `tests/backend/integration/Intelibill.Integration.Tests/BusinessPostingPeriodTests.cs` (date-semantics part; Task 4/6 add the period part).

**Interfaces:**
- Consumes: Task 1 columns/clock, Task 2 postings.
- Produces:
  - Sale/return range methods keep their `DateOnly startDate, DateOnly endDate` signatures but filter `BusinessDate` (sales) / `BusinessDate` or `VoidedBusinessDate` (returns, `GetPostedInRangeAsync`). `GetDailySalesTrendAsync` groups by `BusinessDate`.
  - `IInventoryAdjustmentRepository.GetByShopAndDateRangeAsync(Guid shopId, UtcDateWindow window, CancellationToken)`; purchase order filter takes a `UtcDateWindow?`; customer monthly metrics take a `UtcDateWindow`. Callers build windows with `clock.GetUtcWindow(from, to, shop.BusinessTimeZoneId)`.
  - `ReturnAdjustmentEntry.BusinessDate : DateOnly` (return's `BusinessDate` for a posting, `VoidedBusinessDate` for a reversal); `InRange` uses it.
  - Export row DTOs gain `DateOnly BusinessDate`; renderers print it (sales date column, adjustments posted date). Tally vouchers use `sale.BusinessDate` / `entry.BusinessDate` instead of `BusinessCalendar.DateOf(...)`.
  - Defaults for "today" use `clock.Today(shop.BusinessTimeZoneId)`.

- [ ] **Step 1: Failing integration tests** in `BusinessPostingPeriodTests` (host with a settable `TimeProvider` registered through `ApiWebApplicationFactory`'s `configureTestServices`; reuse the freezable clock pattern from `RefreshRotationConcurrencyTests.FreezableClock` and the shop/tax-profile/stock setup helpers used by `SalesExportsControllerTests` / `CustomerCreditPolicyTests`):
  - `IstEarlyMorning_BelongsToLocalDay`: clock `2026-10-20T20:30:00Z`, post a ₹118 cash sale; `GET api/sales?startDate=2026-10-21&endDate=2026-10-21` contains it, `…2026-10-20…` does not; dashboard/trend bucket is `2026-10-21`; Tally export for 21 Oct has voucher `DATE` `20261021`.
  - `ExactLocalMidnight_StartsNewDay`: sales at `2026-10-20T18:29:59Z` and `2026-10-20T18:30:00Z` appear in 20 Oct and 21 Oct respectively, each exactly once across a 20–21 Oct range.
  - `FiscalYearBoundary_SplitsAtIstMidnight`: sales at `2026-03-31T18:29:59Z` and `2026-03-31T18:30:00Z` → invoice numbers in series `…/2526/…` and `…/2627/…`, business dates `2026-03-31` and `2026-04-01`, credit sale ledger dates equal to those.
  - `TimezoneChange_DoesNotRewritePostedDates`: post at `2026-10-20T20:30Z`; `UPDATE shops SET business_time_zone = 'UTC'` (raw SQL, under the shop's RLS context); the sale stays in the 21 Oct sales list, export and P&L, and its stored `business_date` is unchanged.
  - `JuneSaleJulyReturn_ExportsInOwnPeriods`: sale ₹118 on 2026-06-15 (IST), return on 2026-07-10; June sales export shows the sale with no return adjustment; July export has the return adjustment referencing the June invoice number.
  - `AugustVoid_ReversesJulyReturnInAugust`: void that return on 2026-08-05; July export still shows the return; August shows its reversal; June unchanged.
- [ ] **Step 2: Run, verify red** — `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~BusinessPostingPeriodTests`.
- [ ] **Step 3: Implement** the repository/handler/export changes above; delete every `ToUtcStart` helper.
- [ ] **Step 4: Verify** — the filter above PASS; `--filter "FullyQualifiedName~SalesExportsControllerTests|FullyQualifiedName~ReturnAccountingExportTests|FullyQualifiedName~ProfitLossControllerTests|FullyQualifiedName~SalesControllerTests"` PASS; unit projects PASS. `grep -rn "ToUtcStart\|UtcDateTime)" src/backend/Intelibill.Infrastructure/Repositories` → empty.
- [ ] **Step 5: Commit** `feat(reports): read sales, returns and exports on business dates`.

### Task 4: Posting periods — domain, persistence, fence, close/reopen API

**Files:**
- Create: `Domain/Entities/PostingPeriod.cs`, `Domain/Entities/PostingPeriodEvent.cs`, `Domain/Enums/PostingPeriodState.cs` (`Open = 1, Closed = 2`), `Domain/Enums/PostingPeriodAction.cs` (`Closed = 1, Reopened = 2`), `Domain/Interfaces/Repositories/IPostingPeriodRepository.cs`, domain errors `Domain/Common/Errors.PostingPeriod.cs`
- Modify: `Domain/Enums/FinancialOperationType.cs` (`PostingPeriodClose = 11`, `PostingPeriodReopen = 12`)
- Create: `Application/Common/Posting/IPostingPeriodPolicy.cs`, `Application/Common/Errors/Errors.PostingPeriod.cs`, `Application/Features/PostingPeriods/{Commands/ClosePostingPeriod, Commands/ReopenPostingPeriod, Queries/GetPostingPeriods, DTOs}`
- Create: `Infrastructure/Data/Configurations/PostingPeriodConfiguration.cs`, `PostingPeriodEventConfiguration.cs`, `Infrastructure/Repositories/PostingPeriodRepository.cs`, `Infrastructure/Services/Posting/PostingPeriodPolicy.cs`; DbSets; DI; migration `AddPostingPeriods` (tables + RLS); `TenantPolicyManifest.cs`; `docs/security/tenant-policy-matrix.md`
- Create: `Api/Controllers/PostingPeriodsController.cs`
- Test: `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/PostingPeriodTests.cs`, Application handler tests, `BusinessPostingPeriodTests` (period part), `TenantPolicyManifestTests` stays green

**Interfaces:**
- Produces:
  - `PostingPeriod.Open(Guid shopId, DateOnly anyDateInMonth) : PostingPeriod` (StartDate = first of month, EndDate = last); `static (DateOnly Start, DateOnly End) MonthOf(DateOnly date)`; `ErrorOr<PostingPeriodEvent> Close(Guid actorUserId, DateTimeOffset now, DateOnly today, string reason)` (errors `PostingPeriod.AlreadyClosed`, `PostingPeriod.NotEnded` when `EndDate >= today`, `PostingPeriod.ReasonRequired` for blank / >500); `ErrorOr<PostingPeriodEvent> Reopen(Guid actorUserId, DateTimeOffset now, string reason)` (`PostingPeriod.AlreadyOpen`). Closed state records `ClosedAt`/`ClosedByUserId`; `xmin` row version like `InventoryBatch`.
  - `IPostingPeriodRepository`: `Task EnsureExistsAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken)` (`INSERT … ON CONFLICT (shop_id, start_date) DO NOTHING`); `Task<PostingPeriod?> LockForTransitionAsync(Guid shopId, DateOnly startDate, CancellationToken)` (`FOR UPDATE`, tracked, requires transaction); `Task<IReadOnlyList<PostingPeriod>> GetRangeAsync(Guid shopId, DateOnly from, DateOnly to, CancellationToken)`; `Task<IReadOnlyList<PostingPeriodEvent>> GetEventsAsync(Guid shopId, IReadOnlyCollection<Guid> periodIds, CancellationToken)`; `Task AddEventAsync(PostingPeriodEvent, CancellationToken)`.
  - `IPostingPeriodPolicy.AuthorizeForWriteAsync(Guid shopId, DateOnly businessDate, CancellationToken) : Task<ErrorOr<PostingPeriodLease>>`; `public sealed record PostingPeriodLease(Guid PeriodId, DateOnly StartDate, DateOnly EndDate)`. Throws `InvalidOperationException` without an open transaction (as `TenantWriteScope`). Ensures the row, `SELECT id, state … FOR SHARE`, returns `Errors.PostingPeriod.Closed` (Conflict, code `PostingPeriod.Closed`, description names the month) when closed. Non-Npgsql provider: same logic without the locking clause (mirror `CustomerRepository.LockForCreditAsync`).
  - Commands `ClosePostingPeriodCommand(Guid ShopId, Guid ActorUserId, DateOnly PeriodStart, string Reason, string OperationKey)`, `ReopenPostingPeriodCommand(same)`, both → `ErrorOr<PostingPeriodDto>`. Flow: membership must be active Owner (`Errors.PostingPeriod.OwnerRequired`, Forbidden) → `IFinancialOperationExecutor.ExecuteAsync` (scope type 11/12, `targetId` = shop id; the request hash covers period start + reason) → inside: `EnsureExistsAsync` → `LockForTransitionAsync` → recheck live Owner role → `Close`/`Reopen` → add event → save. `PeriodStart` must be the first of a month (validator).
  - Query `GetPostingPeriodsQuery(Guid ShopId, Guid UserId, int FiscalYearStart)` → `PostingPeriodListDto(int FiscalYearStart, IReadOnlyList<PostingPeriodDto> Periods)` with all 12 months April→March (months without a row reported `Open`), Owner/Manager only.
  - `PostingPeriodDto(DateOnly StartDate, DateOnly EndDate, string State, DateTimeOffset? ClosedAt, Guid? ClosedByUserId, IReadOnlyList<PostingPeriodEventDto> Events)`; `PostingPeriodEventDto(string Action, string Reason, Guid ActorUserId, DateTimeOffset OccurredAt)`.
  - API (`[Authorize]`, shop and user from claims like `PaymentsController`): `GET api/posting-periods?fiscalYear=2026`; `POST api/posting-periods/{period}/close` and `/reopen`, `{period}` = `yyyy-MM`, body `{ "reason": "...", "operationKey": "..." }`.
  - Tables: `posting_periods(id, shop_id, start_date, end_date, state, closed_at, closed_by_user_id, created_at, updated_at…)` unique `(shop_id, start_date)`, check `end_date >= start_date`; `posting_period_events(id, shop_id, posting_period_id FK, action, reason varchar(500), actor_user_id, occurred_at)`. Both RLS FORCE with `*_shop_isolation` policies.

- [ ] **Step 1: Failing domain tests** — `PostingPeriodTests`: `MonthOf(2026-02-14) == (2026-02-01, 2026-02-28)`; close with `today = 2026-07-01` of June succeeds and records actor/time; close of July with `today = 2026-07-31` → `PostingPeriod.NotEnded`; close twice → `AlreadyClosed`; reopen open → `AlreadyOpen`; blank reason → `ReasonRequired`.
- [ ] **Step 2: Failing integration tests** (in `BusinessPostingPeriodTests`):
  - `CloseAndReopen_OwnerOnly_Audited`: manager close → 403; owner close of June (clock 2026-07-05) → 200 `Closed`; list shows June closed with one `Closed` event; owner reopen → `Open` with two events; resending the same `operationKey` replays; same key with different reason → 409 `Operation.PayloadMismatch`.
  - `MissingPeriod_CannotBypassAuthorization`: no `posting_periods` row exists; inside a transaction `AuthorizeForWriteAsync(shop, 2026-06-15)` creates and share-locks the June row; a concurrent close on another connection blocks until that transaction ends (assert via `pg_locks`/timeout, not sleeps alone), and afterwards exactly one June row exists.
  - `PostRacesPeriodClose_CommittedBeforeCloseOrRejected`: two independent scopes/contexts. Schedule A: posting transaction takes the fence (barrier) → close starts and blocks → posting writes a sale and commits → close commits; assert the sale committed with `business_date` in June and the period is Closed afterwards. Schedule B: close transaction holds `FOR UPDATE` (barrier) → posting fence blocks → close commits → posting gets `PostingPeriod.Closed` and no sale/ledger/stock/operation-receipt row exists for it. Assert committed DB state, not only responses.
  - `PostingAfterClosedCheck_CannotCommitLate`: a posting that authorized against Open and is still uncommitted holds the share lock, so a close cannot commit until the posting ends — i.e. no row with `business_date` in a period whose close committed earlier exists (query `sales`/`customer_ledger_entries` vs `posting_period_events.occurred_at`).
- [ ] **Step 3: Run, verify red** — domain unit filter `~PostingPeriod`; integration filter `~BusinessPostingPeriodTests`.
- [ ] **Step 4: Implement** entities, configurations, migration `AddPostingPeriods` (RLS block), manifest + matrix entries, repository, policy, commands/validators/query/handlers (Wolverine handlers like neighbours), errors, controller, DI.
- [ ] **Step 5: Verify** — build clean; domain + application unit PASS; integration `--filter "FullyQualifiedName~BusinessPostingPeriodTests|FullyQualifiedName~TenantPolicyManifestTests|FullyQualifiedName~RestrictedRoleRlsTests|FullyQualifiedName~UpgradedSchemaRlsTests"` PASS.
- [ ] **Step 6: Commit** `feat(periods): monthly posting periods with owner close and reopen`.

### Task 5: Fence sales, offline sync and reconciliation replays

**Files (modify):** `RecordSaleCommandHandler.cs`, `OfflineSalePoster.cs`, `IOfflineSalePoster.cs` (doc), `SyncOfflineSalesCommandHandler.cs`, `ResolveReconciliationCaseCommandHandler.cs` (only if needed), `Domain/Enums/ReconciliationIssueType.cs` (`ClosedPeriodReview = 12`), `ReconciliationActionPolicy.cs` (add `ClosedPeriodReview` to `HeldTypes`, `Errors.PostingPeriod.Closed.Code` to `ReplayableHoldCodes`), web reconciliation issue-type label if the console maps types to i18n keys (`src/frontend/public/assets/i18n/*.json` — add the key in every locale so `i18n-coverage.spec.ts` stays green).

**Interfaces:**
- Consumes: `IPostingPeriodPolicy`, `Errors.PostingPeriod.Closed` (Task 4), business days (Task 2).
- Produces: `OfflineSalePostingOutcome.Held(ReconciliationIssueType.ClosedPeriodReview, Errors.PostingPeriod.Closed, …)` from `StageAsync` when the claimed business date's period is closed.

- [ ] **Step 1: Failing tests** — unit: `RecordSaleCommandHandlerTests.ClosedPeriod_RefusesSale_BeforeCreditLockOrNumber` (policy fake returns Closed → result `PostingPeriod.Closed`, no invoice number allocated, no customer lock); `SyncOfflineSalesCommandHandlerTests.ClosedPeriod_HoldsInvoice_AsClosedPeriodReview` (nothing staged, case filed with that type and code, sale's invoice number unchanged); `ReconciliationActionPolicyTests` offers `RetryHistoricalLease` for an enveloped `ClosedPeriodReview` case. Integration: `LateOfflineInvoice_RequiresExplicitResolution` — June closed, sync an offline invoice issued 2026-06-30 23:00 IST from a valid lease → `NeedsReview` with `reviewCaseId`, no sale row; resolve with `RetryHistoricalLease` while closed → 409/ReplayRejected carrying `PostingPeriod.Closed`, case open; owner reopens June; retry → sale posted with `business_date 2026-06-30`, the **original** invoice number and `sold_at` unchanged.
- [ ] **Step 2: Run, verify red.**
- [ ] **Step 3: Implement** — `RecordSale`: right after `BeginTransactionAsync`, `AuthorizeForWriteAsync(shopId, businessDay.Date)`; error → return it (finally block rolls back). `OfflineSalePoster.StageAsync`: first statement fences the claimed business date; `Closed` → `Held(ClosedPeriodReview, …)`, other errors → `Rejected`. `SyncOfflineSalesCommandHandler.SyncSaleAsync`: open a transaction immediately before `StageAsync`, commit after the successful save; on `Held`/`Rejected`/`DbUpdateException` roll back first, then run the existing hold/error path (the hold case saves outside the rolled-back transaction). Reconciliation replay already runs inside the executor's transaction; `Held` maps to `ReplayRejected` + the held error as today.
- [ ] **Step 4: Verify** — unit projects PASS; integration `--filter "FullyQualifiedName~BusinessPostingPeriodTests|FullyQualifiedName~OfflineReconciliationControllerTests|FullyQualifiedName~SalesControllerTests|FullyQualifiedName~CustomerCreditPolicyTests"` PASS; if i18n touched: `cd src/frontend && bun run test --run src/app/core/i18n`.
- [ ] **Step 5: Commit** `feat(periods): fence sales and hold late offline invoices for closed periods`.

### Task 6: Fence returns, payments, expenses and stock/purchase postings

**Files (modify):** `SaleReturnMutationCoordinator.cs` (record + void), `RecordCustomerPaymentCommandHandler.cs`, `MakeSupplierPaymentCommandHandler.cs`, `ReversePaymentCommandHandler.cs`, `RecordExpenseCommandHandler.cs`, `CorrectExpenseCommandHandler.cs`, `AddInventoryCommandHandler.cs` + the bulk intake row path (`IInboundRowExecutor` implementation), `ReceivePurchaseOrderCommandHandler.cs`, `CorrectReceiptCostCommandHandler.cs`, `VoidBatchCommandHandler.cs`, inventory adjustment record/void handlers; plus their unit tests.

**Interfaces:**
- Consumes: `IPostingPeriodPolicy`, `IBusinessClock`.
- Rule: the fence is the first statement inside the transaction (inside the executor action where an executor is used; otherwise wrap the existing save in `BeginTransactionAsync`/`CommitTransactionAsync`/rollback-in-finally, as `PaymentTransactionRunner.RunUnkeyedAsync` does). Fence date: payments → `command.PaymentDate`; expense record → `ExpenseDate`; expense correction → both the original and the new date's periods, distinct, ascending; everything else → `clock.Today(zone)` of the posting instant.

- [ ] **Step 1: Failing tests** — unit, one per handler family: closed fence → `PostingPeriod.Closed`, nothing saved (repository `AddAsync` not called / unit of work not saved). Integration `ClosedPeriod_BlocksBackdatedPost_AllowsLinkedCurrentCorrection`: June sale ₹118 (credit customer), close June (clock in July); backdated expense dated 2026-06-20 → 409 `PostingPeriod.Closed`; customer payment dated 2026-06-25 → 409; return of the June sale today (July) → 200, return `business_date` in July, referencing the June invoice; June sales export and P&L for June unchanged (byte-for-byte same totals as before the close).
- [ ] **Step 2: Run, verify red.**
- [ ] **Step 3: Implement** per the rule above. Keep each handler's existing lock order after the fence (customer → sale → credit notes → batches).
- [ ] **Step 4: Verify** — unit projects PASS; integration `--filter "FullyQualifiedName~BusinessPostingPeriodTests|FullyQualifiedName~ExpensesControllerTests|FullyQualifiedName~PaymentAllocationTests|FullyQualifiedName~SaleReturnConcurrencyTests|FullyQualifiedName~InventoryControllerTests|FullyQualifiedName~PurchaseOrdersControllerTests|FullyQualifiedName~PurchaseCostLineageTests|FullyQualifiedName~InventoryAdjustmentControllerTests|FullyQualifiedName~FinancialOperationTests"` PASS.
- [ ] **Step 5: Commit** `feat(periods): fence returns, payments, expenses and stock postings`.

### Task 7: Documentation and full verification

**Files (modify):** `docs/backend-architecture.md` (new `## Business Dates and Posting Periods`: zone, stored dates, windows, fence and lock order, close/reopen, late offline, rollout trigger + backfill script, rollback), `AGENTS.md` (Key endpoints: posting periods; Infrastructure bullet; entities/enums lists; test snapshot counts), `docs/security/tenant-policy-matrix.md` (if not done in Task 4).

- [ ] **Step 1:** Write the docs.
- [ ] **Step 2: Full verification** — `dotnet build src/backend/Intelibill.slnx` (0 warnings); `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests`, `…Application.Unit.Tests`, `…Api.Unit.Tests`; `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests` → all PASS; record counts in AGENTS.md snapshot.
- [ ] **Step 3: Commit** `docs: business dates and posting periods`.

## Rollout and rollback

1. Deploy migration `AddBusinessDatesAndShopTimeZone` (nullable → backfill → NOT NULL + triggers) and `AddPostingPeriods`; then pods. Old pods keep working: the triggers date their inserts. After rollout run `scripts/db/backfill-business-dates.sql` (no-op when nothing is NULL).
2. All periods start Open (no rows). Owners close months after review; nothing is closed automatically.
3. Previously published reports that straddled UTC/IST midnight may differ by early-morning sales; note it in release notes.
4. Rollback: the old code ignores the new columns and tables; never recompute stored dates with the UTC convention. Dropping the migration drops the columns (the instants are untouched).

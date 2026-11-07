# Sale Cancellation Policy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct eligible mistaken online cash/due receipts exactly once while retaining original documents and explicit stock, account, and refund evidence.

**Architecture:** An immutable cancellation aggregate and terminal sale status, posted by the existing financial operation executor under the existing customer→sale lock. Separate dated cancellation projections integrate balances, history, P&L, dashboard, all exports and print; the web freezes operation identity before sending and recovers uncertain outcomes.

**Tech Stack:** ASP.NET Core 10, C#/ErrorOr/EF Core/PostgreSQL, xUnit/Testcontainers, Angular 21/NgRx/PrimeNG, Bun and IndexedDB. No new packages.

**Spec:** `docs/superpowers/specs/2026-11-08-sale-cancellation-policy-design.md` (read fully before execution).

## Global Constraints

- Web and API for Indian retail public SaaS; mobile is excluded.
- Keep Domain ← Application ← Infrastructure ← Api, ErrorOr, repository abstractions, IUnitOfWork, decimal money, warnings-as-errors, Bun frontend tooling, and existing offline invoice arithmetic/document contracts.
- Policy `receipt-cash-due/1`: online v2 unregistered zero-tax Receipt, same open shop-local month, all goods recovered, no return history, credit-note application/redemption or collection ambiguity; positive paid amount requires Cash and explicit paid-back confirmation.
- `SaleCancellation:Enabled=false`, `SaleCancellation:AllowedShopIds=[]`; new writes require both. Preserve preview/read/replay after disablement. Do not enable a production shop.
- Reason trimmed 3–255; key trimmed 1–128; expected version nonzero; original invoice number/snapshot/amounts/identifiers never change.
- Unique cancellation per sale, FORCE RLS and tenant composite FKs; append enum numbers 17 operation, 6 customer ledger, 10 stock movement without renumbering.
- This plan is already authorized for immediate Sol implementation followed by independent review/fixes and PR; no redundant approval pause. Implement in the existing feature branch. No production migration/deployment is authorized by this planning document.

## Review Focus

1. A later collection without allocations still changes account debt: Task 2 checks unallocated receipts and full due coverage; Task 3 races collection under the customer lock.
2. A stale pre-lock EF entity can defeat correct SQL locking: Task 2 loads current tracked facts only after locking; Task 3 proves both race orders with separate contexts.
3. Fractional cost/quantity and cancellation-only report windows can leave hidden residuals: Task 4 tests exact rounded COGS and windows without original sale rows.
4. A timed-out operation followed by 404/403 must not become a fresh operation or a fake cash payout: Task 5 tests frozen recovery, double tabs and storage failures.
5. Future deployment rollback may permit returns against a cancelled sale: Tasks 3 and 6 guard all mutation paths and document disable-writes/preserve-readers rollback.

---

## Implementation contracts

Paths below use `src/backend/Intelibill.` + `Domain`, `Application`, `Infrastructure`, or `Api` as the backend project roots; frontend `features/` and `core/` paths are under `src/frontend/src/app/`. Test projects are under `tests/backend/unit/`; integration fixture names are under `tests/backend/integration/Intelibill.Integration.Tests/`. Explicit full paths override these abbreviations. Existing code was explored on `569a763b`; use CodeGraph before changing shared symbols and inspect current diffs before starting a task. Keep the tasks cohesive; steps include their own tests and commits.

**Domain:** `Sale.Status : SaleStatus`, `Sale.Version : uint`, `Sale.MarkCancelled() : ErrorOr<Success>`; immutable `SaleCancellation`/`SaleCancellationLine`. Use `SaleCancellation.Record(SaleCancellationInput input) : ErrorOr<SaleCancellation>` with typed input containing the exact snapshot/effect fields in the spec; line input uses `SaleCancellationLineInput`. Factories validate totals, positive quantities, links and confirmations; never accept an externally supplied actor/shop/date.

**Repository:** `ISaleCancellationRepository : IRepository<SaleCancellation>` exposes `GetBySaleAsync(Guid shopId, Guid saleId, CancellationToken)`, `GetBySaleIdsAsync(Guid shopId, IReadOnlyCollection<Guid> saleIds, CancellationToken)`, and `GetPostedInRangeAsync(Guid shopId, DateOnly from, DateOnly to, CancellationToken)`, all including frozen lines. Fresh sale retrieval after lock is separate from authorization's existence read.

**Application:** `ISaleCancellationRollout.IsPostingEnabled(Guid shopId) : bool`; `SaleCancellationPolicy.EvaluateAsync(Guid shopId, Sale sale, BusinessDay day, CancellationToken) : Task<SaleCancellationEvaluation>` returns authoritative data/reasons using a sale loaded by the caller. `SaleCancellationService.PreviewAsync(Guid actorUserId, Guid shopId, Guid saleId, CancellationToken) : Task<ErrorOr<SaleCancellationPreviewDto>>`; `CancelAsync(Guid actorUserId, Guid shopId, Guid saleId, CancelSaleRequest request, CancellationToken) : Task<ErrorOr<SaleCancellationDto>>`. These are the API's single entrypoints; split mutation mechanics into a focused helper if needed rather than a giant service.

**DTOs:** `CancelSaleRequest(string OperationKey, uint ExpectedVersion, string Reason, bool GoodsRecoveredConfirmed, bool CashRefundConfirmed)`. Preview contains SaleId, InvoiceNumber, Document, Status, ExpectedVersion, Eligible, PostingEnabled, BusinessDate, Reasons(code,message), Lines(original ids/quantity/description/UQC), TotalAmount, DueReductionAmount, CashRefundAmount, ExternalRefundAmount, CreditRestorationAmount, RequiresGoodsConfirmation and RequiresCashRefundConfirmation. Cancellation DTO exposes Id, SaleId, Reference, Reason, ActorUserId, CancelledAt, BusinessDate/zone, PolicyVersion, original invoice number/date, totals/settlement facts, lines and ledger/movement links. `SaleDto` adds Status, Version and nullable Cancellation without removing fields.

**Accounting:** `SaleCancellationAdjustmentReader.ReadAsync(Guid shopId, DateOnly from, DateOnly to, CancellationToken) : Task<IReadOnlyList<SaleCancellationAdjustmentEntry>>`; entry carries cancellation/sale identity, frozen original invoice/date/customer, posting date/instant, reason, line snapshots, revenue, raw original cost, rounded recorded COGS, due reduction and confirmed cash refund. All values are positive magnitudes; consumers negate revenue/cost when building adjustment rows. Use this single projection across P&L and exports, not a fabricated ReturnAdjustmentEntry.

### Task 1: Persist immutable cancellation facts and terminal sale state

**Files:**
- Create `src/backend/Intelibill.Domain/Entities/Sale.Cancellation.cs`, `SaleCancellation.cs`, `SaleCancellationLine.cs`; `Domain/Enums/SaleStatus.cs`; `Domain/Interfaces/Repositories/ISaleCancellationRepository.cs`.
- Create `src/backend/Intelibill.Infrastructure/Data/Configurations/SaleCancellationConfiguration.cs`, `SaleCancellationLineConfiguration.cs`; `Infrastructure/Repositories/SaleCancellationRepository.cs`.
- Modify `Domain/Enums/{FinancialOperationType,CustomerLedgerEntryType,StockTransactionType}.cs`, `Domain/Entities/{CustomerLedgerEntry,StockTransaction}.cs`, `Infrastructure/Data/Configurations/{SaleConfiguration,SaleItemConfiguration,CustomerLedgerEntryConfiguration,StockTransactionConfiguration}.cs`, `Infrastructure/Data/ApplicationDbContext.cs`, both layer `DependencyInjection.cs` files, `Application/Features/FinancialOperations/FinancialOperationTypes.cs`.
- Generate `Infrastructure/Migrations/<timestamp>_AddSaleCancellations.cs`, designer and model snapshot. Reuse existing stock/batch composite keys; add required sale/sale-item/customer-ledger keys. Update constrained enum storage in the same migration.
- Tests: create `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/SaleCancellationTests.cs`; extend `CustomerLedgerEntryTests.cs` and `StockTransactionTests.cs`; create integration `SaleCancellationSchemaTests.cs` beside `RestrictedRoleRlsTests.cs`.

- [ ] Write factory/sign tests: ₹200 = ₹80 cash + ₹120 due; zero due has no ledger link; zero cash cannot claim refund; mismatch, missing links, duplicate line and nonpositive quantity rejected. `Sale.MarkCancelled` preserves serialized issued document and amounts; second call refuses. Cancellation ledger has −₹120 delta; movement +2 is valid and −2 invalid.
- [ ] Run `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter 'FullyQualifiedName~SaleCancellation|FullyQualifiedName~CustomerLedgerEntry|FullyQualifiedName~StockTransaction'`; capture expected missing-contract failures.
- [ ] Implement the domain/repository contracts above and generated migration. Map `uint Version` with `IsRowVersion()` to PostgreSQL xmin; do not create a physical xmin column. Existing sales default Posted. Add SQL checks and Restrict/composite FKs/RLS; retain same-shop sale-item/cancellation association, not merely individually valid foreign IDs.
- [ ] Add real-DB schema tests: one cancellation per sale, cross-shop header/line/effect links rejected, restricted role cannot read or insert another shop's rows, blank active shop sees none, legacy rows read Posted with nonzero version. Keep tests possible with direct fixture factories before HTTP exists.
- [ ] Run the focused domain tests and `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~SaleCancellationSchemaTests`; inspect migration SQL for both new tables' USING/WITH CHECK and enum constraints; commit `feat: add sale cancellation facts`.

### Task 2: Add preview and atomic cancellation API

**Files:**
- Create `Application/Features/Sales/DTOs/SaleCancellationDtos.cs`; `Services/Cancellations/{SaleCancellationPolicy,SaleCancellationService,ISaleCancellationRollout}.cs`; `Application/Common/Errors/Errors.SaleCancellation.cs`.
- Create `Infrastructure/Options/SaleCancellationOptions.cs`, `Infrastructure/Services/Sales/SaleCancellationRollout.cs`; register/configure in the owning DI files.
- Create `Api/Controllers/SalesController.Cancellations.cs`; request shape is the contract above and validation uses the existing FluentValidation/API conventions.
- Modify `Infrastructure/Repositories/SaleRepository.cs` / `Domain/Interfaces/Repositories/ISaleRepository.cs` only if a fresh no-tracking existence read is needed; reuse `LockForReturnMutationAsync` inside the executor.
- Create `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Sales/Cancellations/SaleCancellationPolicyTests.cs`; create integration `SaleCancellationTests.cs`.

- [ ] Write API red tests: 2 units/₹200 cash restores exactly 2 and retains invoice; pure ₹200 due restores account without cash; ₹80 cash + ₹120 due reverses exact legs; same batch appearing on two sale lines restores total once; staff and foreign tenant refused before result lookup; goods/cash confirmations, reason/key/version validation.
- [ ] Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~SaleCancellationTests`; verify endpoint/behavior failures before implementing.
- [ ] Implement policy with all ten rules in the spec, including returned-then-voided history, frozen seller policy, snapshot/header/line money consistency, unresolved credit-note redemptions, noncash tender even when cash checkbox is true, active allocations, unallocated receipt ambiguity and insufficient account balance. Preview must show exact unresolved obligations while remaining side-effect free: use `IPostingPeriodRepository.GetRangeAsync`, treating an absent row as open, and never invoke the writing fence from preview. Tests must assert zero added rows/movements on every refusal.
- [ ] Implement `PreviewAsync` and `CancelAsync` with explicit live membership checks before executor/recovery, canonical payload hash, single captured instant/day, gate→period→customer→sale→batch ordering, fresh post-lock entities, xmin conflicts, all-effects SaveChanges and executor-owned commit. Snapshot original raw and rounded cost separately. Translate PostgreSQL lock/serialization/deadlock outcomes after rollback. Publish existing inventory change event only after success; retries must not perform stock/account mutations.
- [ ] Configure default-disabled and empty allowlist. Tests override both for eligible fixtures. Test disabled new write=503 but preview is readable and an already committed same-key replay/recovery succeeds after disablement/period closure. Add slug registration for `sale-cancellation` recovery.
- [ ] Add cases `TaxInvoiceBlocked`, `LegacySnapshotBlocked`, `ServiceBlocked`, `OfflineOriginBlocked`, `OtherMonthBlocked`, `ClosedMonthBlocked`, `ExternalRefundPendingIsNeverPaidBack`, `AppliedCreditRequiresRestoration`, `UnallocatedCollectionBlocks`, `AllocatedCollectionBlocks`, `RefundConfirmationCannotTurnCardIntoCash`, `StaleVersionConflicts`, `SameKeyReplays`, `ChangedPayloadConflicts`, `DifferentKeyAlreadyCancelled`, and transaction failure rollback. Use frozen clock fixtures for month/midnight boundaries.
- [ ] Run focused domain/application/API integration fixtures; commit `feat: post eligible sale cancellations`.

### Task 3: Close competing mutation paths and expose truthful status/balances

**Files:**
- Modify `Application/Features/Sales/Services/Returns/SaleReturnValidator.cs`, `Queries/GetSaleDetail/GetSaleDetailQueryHandler.cs`, `Commands/RecordSale/SaleDtoBuilder.cs`, `DTOs/SaleDto.cs`.
- Modify `Infrastructure/Services/Payments/InvoiceBalanceReader.cs`; `Application/Features/Payments/DTOs/PaymentDtos.cs`; `Application/Features/Customers/DTOs/CustomerAccountSaleDto.cs`, `Queries/GetCustomerAccount/GetCustomerAccountQueryHandler.cs`.
- Modify `Infrastructure/Repositories/SaleRepository.cs`, corresponding repository read models and `Application/Features/Sales/Queries/GetSales` DTO mapping; dashboard latest-sales DTO mapping.
- Verify `Commands/SyncOfflineSales/{SyncOfflineSalesCommandHandler,OfflineSalePoster}.cs` deduplication; only make changes if a test exposes reposting/terminal-state loss.
- Tests: create integration `SaleCancellationConcurrencyTests.cs`; extend `SaleCancellationTests.cs`, `SaleReturnConcurrencyTests.cs`, payment allocation/customer credit/account fixtures, `SaleDtoBuilderTests.cs`.

- [ ] Write race tests with separate DB contexts and controlled lock holders, both winning orders: return↔cancellation, allocated collection↔cancellation, unallocated collection↔cancellation, two keys↔one sale, same key↔same payload. Assert exact final stock, one terminal correction, zero duplicate ledger credits/refunds, no hanging transaction. Add batch-count/stock-save concurrency rollback.
- [ ] Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter 'FullyQualifiedName~SaleCancellationConcurrencyTests|FullyQualifiedName~SaleReturnConcurrencyTests'`; ensure a missing terminal guard fails before adding it.
- [ ] Reject Cancelled in the return validator after its lock, expose zero returnable quantity, and ensure allocations see zero outstanding after customer→sale lock. Extend balance DTO with explicit CancellationCreditAmount rather than using ReturnCreditAmount. Ledger/current outstanding/overdue/available credit all agree. Applied or issued-and-redeemed credit-note cases must refuse without changing note balance/redemption history.
- [ ] Add SaleDto status/version/cancellation to detail and online replay builders. History status `cancelled` takes precedence; other status filters exclude cancelled, original issue fields stay frozen. Customer statements show cancellation credit distinctly; dashboard latest sales show status. Do not filter terminal sales out of shared date-range repositories.
- [ ] Test RecordSale original key after cancellation returns the original sale and cancellation state without consuming stock. Test offline-origin preview refusal; defensive existing-cancelled offline duplicate returns existing identity without posting (leave arithmetic/version/queue contracts intact).
- [ ] Run focused integration fixtures plus affected unit tests; commit `fix: protect cancelled sale balances and actions`.

### Task 4: Integrate dated cancellation economics and every export

**Files:**
- Create `Application/Features/Exports/Accounting/SaleCancellationAdjustmentEntry.cs`, `SaleCancellationAdjustmentReader.cs`.
- Modify `Application/Features/Sales/Queries/GetProfitLossReport/ProfitLossReportBuilder.cs`, query validator and `DTOs/{ProfitLossReportItemDto,ProfitLossReportResultDto}.cs`; `Application/Features/Exports/ProfitLoss/Queries/ExportProfitLoss/ExportProfitLossQueryHandler.cs` and DTO mapping as needed.
- Modify `Infrastructure/Repositories/SaleRepository.cs` history summary, daily trend, customer metrics and associated read models/DTOs; `Application/Features/Dashboard/Queries/GetDashboard/GetDashboardQueryHandler.cs` for explicit cancellation amounts/net trend.
- Modify `Application/Features/Exports/Sales/Services/SalesExportDatasetBuilder.cs`, `DTOs/{SalesExportDatasetDto,SalesExportSummaryRowDto,SalesExportLineItemRowDto,SalesExportTaxBreakupDto}.cs`.
- Modify `Infrastructure/Services/Exports/{SalesExcelExportRenderer,SalesExcelSummaryRenderer,SalesExcelLineItemRenderer,SalesPdfExportRenderer,SalesTallyXmlExportRenderer,TallyVoucherBuilder,TallyLedgerBuilder,ProfitLossExcelExportRenderer}.cs`; create focused `SalesExcelCancellationRenderer.cs` if the renderer split merits it.
- Tests: `Application.Unit.Tests/Features/Sales/Queries/GetProfitLossReport/ProfitLossEconomicsTests.cs`, `Features/Exports/Sales/SalesExportDatasetBuilderTests.cs`, existing API unit renderer tests; create integration `SaleCancellationReportingTests.cs`.

- [ ] Add red fixtures sale day Nov 8, cancellation day Nov 9 in the same month: original ₹200 / cost ₹120, windows original-only (+200/+120), cancellation-only (−200/−120), both (0/0). Assert zero independent stock loss, return count unchanged, cancellation count=1, original invoice count retained. Add quantity 0.333 × cost ₹1.01 and multiple lines to prove sum-of-rounded-original-cost reversal with no residual; preserve legacy raw-cost reversal independently.
- [ ] Run affected report/builder unit tests, then implement the dedicated reader and `saleCancellation` rows/filter/search. Add cancellation amount/count separately to summaries; date filters use cancellation BusinessDate. Customer lifetime/month revenues and dashboard net trends include negative cancellation postings without erasing original dates. Existing return semantics remain unchanged.
- [ ] Add cancellation entries to export dataset. XLSX/PDF retain original amount/status and show separate reversal sections and correct net totals, even without original rows in the date window; cancellation amounts do not inflate return columns. Tax-breakup net revenue must reconcile for zero-tax receipts. P&L XLSX maps new row type and exact economics.
- [ ] Add `TallyVoucherBuilder.BuildCancellationVoucher(SaleCancellationAdjustmentEntry entry) : AccountingVoucher`: Journal debits Sales ₹200, credits Cash ₹80 + original customer ₹120. Validate with `IAccountingVoucherValidator`, include new ledgers and original reference date. Never use GST Credit Note type, invent tax, or choose current customer names.
- [ ] Test all three renderers and P&L XLSX with cancellation-only data; inspect journal balance and reference, report totals, cancellation labels, fractional costs, and changed shop/customer/batch metadata. Add explicit preservation tests for ordinary sales/returns.
- [ ] Run `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter 'FullyQualifiedName~ProfitLoss|FullyQualifiedName~SalesExport|FullyQualifiedName~Dashboard'`, API unit renderer filters, and integration `SaleCancellationReportingTests`; commit `feat: report dated sale cancellation effects`.

### Task 5: Deliver durable web cancellation, history and print

**Files:**
- Create `src/frontend/src/app/features/sales/services/sale-cancellation.models.ts`, `sale-cancellation.service.ts`, `sale-cancellation-draft.service.ts` with matching specs; model after supplier-return draft CAS/unknown/tombstone semantics without refactoring that feature.
- Create `features/sales/components/sale-detail/sale-cancellation-dialog.component.{ts,html}` and spec; integrate `components/sale-detail-overlay.component.{ts,html}`.
- Modify `features/sales/services/{sale.models,sale.service}.ts`, `state/sales.*.ts` where detail/history invalidation requires; core endpoint/operation-type declarations and `REQUEST_SCOPE` bindings.
- Modify `features/sales/pages/{sales-page.component,profit-loss-page/profit-loss-page.component,sale-invoice-print-page/sale-invoice-print-page.component}.{ts,html}` and specs; dashboard/customer view models/templates for new explicit status/amounts/ledger label. Modify every locale under `src/frontend/public/assets/i18n/` for introduced keys.
- Tests: draft/service/dialog specs, existing history/print/P&L/customer specs; add cancellation scenario to existing UI audit fixture/route coverage without creating a new route unnecessarily.

**Interfaces:** `SaleCancellationService.preview(saleId): Observable<SaleCancellationPreviewDto>`; `cancel(saleId, request, owner: ScopeOwner): Observable<SaleCancellationDto>` with owner-bound REQUEST_SCOPE. `SaleCancellationDraftService` offers load/save/prepare/claimRecovery/markUnknown/settleFailure/settle/isConfirmed/clearConfirmed/discard with the same argument semantics as SupplierReturnDraftService, typed to cancellation payload and a separate IndexedDB database/store. No fresh key until the previous intent is definitively resolved.

- [ ] Write draft/service red tests using fake-indexeddb: prepare freezes payload/key before send; storage failure prevents HTTP; simultaneous tabs use one key; pending cannot be edited/discarded; reload reuses payload; recovery404 keeps unknown; unknown followed by403/409 retains uncertainty; a stale response cannot clear replacement state; foreign scope cannot send or render completion; tombstone survives cleanup.
- [ ] Run `bun run test --run 'src/app/features/sales/services/sale-cancellation*.spec.ts'` from `src/frontend`; implement the durable store and scoped HTTP/recovery wrapper using the contracts above. Never queue cancellation as an offline sale.
- [ ] Implement preview dialog: reason, original document/reference, every eligibility reason, recovered quantities, exact debt/refund obligations, independent unchecked confirmations, posting-disabled banner, frozen recovery UI with Check status / Retry same operation. Existing pending operation recovery takes precedence over fetching a new preview. A 404 lookup does not permit editing/new submit.
- [ ] Integrate owner/manager action and success refresh through existing NgRx/detail paths. Cancelled sales suppress return/cancel actions, show linked facts, and filter as cancelled. Disable action for local-only/offline invoice; noncash and credit blocks explain no refund/restoration occurred. P&L filters/labels and dashboard/customer amounts render the new backend fields.
- [ ] Preserve original frozen receipt print; add separate cancellation banner and full cancellation reference/settlement facts in both A4 and thermal layouts. Render exact spec disclaimer; zero-cash version omits cash sentence. Master-data edits must not change printed facts.
- [ ] Add dialog/print tests for staff, disabled rollout, all blocked obligations, unchecked checkbox behavior, mixed cash/due result, reload/scope recovery and A4/thermal output. Run targeted specs then `bun run test --watch=false`; commit `feat: add durable sale cancellation workflow`.

### Task 6: Verify rollout safety, complete docs, review and PR

**Files:** `AGENTS.md`, `docs/backend-architecture.md`, `docs/frontend-architecture.md`, new `docs/sale-cancellation-rollout.md`; integration fixtures/RLS inventory lists and UI audit fixtures if required.

- [ ] Document policy boundary, endpoints, status/version, locks, operation17/ledger6/stock10, snapshot economics, explicit unsupported cases, RLS migration order, config defaults/allowlist and commercial disclaimer. The concrete rollout doc names the generated migration and instructs all API pods/readers to update before any approved shop is enabled. Do not claim accountant/legal approval.
- [ ] Document rollback as disabling new writes while retaining schema/history/replay and cancellation-aware mutation guards. Explain why deploying pre-feature binaries or dropping facts after any cancellation is unsafe. Add the default-disabled config test to release acceptance and do not edit production configuration.
- [ ] Run `dotnet build src/backend/Intelibill.slnx`, `dotnet test src/backend/Intelibill.slnx` with Docker, and frontend `bun run test --watch=false`, `bun run build`, `bun run audit:ui:release`. Use a Node version within `>=24 <26`; Node24 is available via `PATH=/tmp/intelibill-sale-cancellation-node24/node_modules/.bin:$PATH` for frontend commands in this session. Verify `node --version` after selecting it. Do not suppress audit/analyzer warnings.
- [ ] Inspect generated SQL and migration application on the real integration database, verify all new tenant tables in the restricted-role suite, and inspect at least A4/thermal print plus dialog in the actual rendered UI audit. No production DB application here.
- [ ] Request independent Sol implementation review and final Astra review per user's chosen workflow. Review specifically auth-before-replay, stale tracked entities, both race orders, new ledger type consumers, zero/noncash/applied-credit refusal, all direct sale aggregates, all export formats, default-disabled rollout, and unknown web recovery. Fix actionable comments, rerun affected checks, then broad checks only when new risks justify them.
- [ ] Commit final docs/fixes; push `codex/sale-cancellation-policy` and raise PR. PR leads with mistaken receipt correction, states receipt-only/cash-or-due restrictions and default-disabled shop allowlist, names migration prerequisite, lists actual validation evidence and remaining limitations. Do not enable live posting or deploy.

## Self-review completed

The supplied plan's cash/due correction, idempotency, race protection, external pending-refund honesty, applied-credit handling, issued-document retention, history/print/report/export, tenancy and migration requirements all have explicit tasks. Unsupported external/credit/GST/offline/service cases are visible policy refusals, not omitted behavior. API/DTO/repository/reader contracts are shared across tasks; Tasks 1–3 establish backend facts and guards before readers/UI. Implementation may split a file or rename a private helper to fit established patterns, but may not broaden eligibility, erase facts, collapse cancellation into return accounting, or weaken unknown-outcome recovery without updating spec and tests.

# Profit and discount economics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. The user requested Astra analysis/planning followed by Sol implementation and independent review/fixes, and authorized continuing without an approval pause.

**Goal:** Enforce tax-exclusive discount protection against recorded cost and publish clearly defined trading/operating figures without double-counting supplier settlements, returns, or stock losses.

**Architecture:** Keep online pricing on its existing shared calculator; introduce a small pure economics calculator and additive, versioned report projections. Classify expenses explicitly and derive reports from frozen sale/return costs, dated stock-loss postings, and active expense facts. Preserve frozen offline arithmetic and all historical invoice amounts.

**Tech Stack:** .NET 10, C#, ErrorOr, FluentValidation, EF Core/Npgsql, PostgreSQL, xUnit/Testcontainers; Angular 21, NgRx, PrimeNG, Transloco, Bun/Vitest.

**Spec:** `pending-tasks/28-profit-and-discount-economics-done.md` (the ignored backlog source was renamed externally during execution); authoritative user attachment `/Users/chanakya/.codex/attachments/648b66a8-8809-4278-9d45-6d4c2196a217/Pasted text.txt`. This current-source plan supersedes stale implementation assumptions in that spec, not its intended outcome.

## Global Constraints

- Web/API only; mobile excluded. Preserve Domain ← Application ← Infrastructure ← Api, ErrorOr, repository abstractions and `IUnitOfWork`.
- Decimal money; round money to two decimals, midpoint away from zero. Quantity supports the item's scale up to three decimals. C# warnings remain errors; package versions remain centralized.
- Tenant and actor come from authenticated scope. Existing owner/manager controls remain; no new below-cost override permission.
- Never infer recoverable purchase tax from `PurchaseTaxIncluded`, GST registration, or sales tax rate. Never rewrite frozen historical costs, tax amounts, documents, queues, or idempotency hashes.
- Reports describe recorded-cost trading economics, not a general ledger, audited profit, or income-tax result. Purchase-tax recovery and service costs are not modeled.
- Frontend uses Bun. Real PostgreSQL/Docker integration and restricted-role tenancy coverage are required; migrations are explicit deployment work, never API startup work.
- Work on `codex/profit-discount-economics`, based on `dfae39e6`. Parent owns commit/PR orchestration and baseline verification; do not repeat completed baselines without a reason.

## Current-source analysis and decisions

Inspected on 2026-11-07. CodeGraph was used for broad exploration, symbol lookup and blast radius; it identified shared consumers but some queries returned unrelated frontend symbols, so uncovered backend files were read directly.

1. `SalePricingMath.CalculateRevenueForCostCheck` adds output tax for inclusive prices. `CalculateDiscountCapacity` likewise derives capacity from gross receipts. `SalePricingCalculator` already drives `PreviewSaleQueryHandler` and `RecordSaleCommandHandler`, rejects below-cost lines regardless of actor, and has a capacity-capped cent allocator. Keep those shared entry points and existing error codes.
2. A second defect precedes that calculator: `InventoryBatch.GetProfitCostPrice()` strips tax from `CostPrice` whenever `PurchaseTaxIncluded` is true, using the batch's sales tax rate. Its three production callers are preview, recording, and current-stock valuation. No recoverability evidence exists in `InventoryBatch` or `PurchaseReceiptValue`. New calculations must use the recorded `CostPrice` unchanged. Existing `SaleItem.CostPrice`/return `OriginalCostPrice` stay immutable. Do not claim that recorded purchase amounts are verified tax-exclusive economic cost; explicitly disclose the missing policy.
3. Offline pricing v1 has an uncapped largest-remainder allocation frozen by `tests/contracts/offline-invoice-arithmetic-v1.json`. Offline snapshot/finalization uses raw batch `CostPrice`, unlike online today. Do not change v1 arithmetic or add a new calculation version just to enforce issuance policy: evaluate the computed lines and block unsafe *new* offline issuance before consuming a number, queue entry, or shadow stock. A cart whose v1 proportional allocation breaches one line may be refused offline even if online can redistribute; explain that it must be repriced/discount reduced or completed online. Previously issued invoices still replay unchanged and acquire a review warning/case if below recorded cost.
4. `ProfitLossReportBuilder` uses frozen sales and `ReturnAdjustmentReader` (plan 17), which already emits return and void postings in their own business periods (plan 27). Its `MarginPercent`/`AverageMarginPercent` are profit/cost (markup), and `NetProfitAfterTax` is tax-exclusive trading profit before operating expenses. Preserve these legacy fields and formulas; add explicitly named economics fields with `Version = 2`. Do not silently repurpose old names.
5. Return wastage is currently diagnostic: original sold COGS remains consumed and only restockable return cost reverses. Subtracting `WastageCost` a second time would be wrong. New `StockLoss` includes independent inventory decreases only; separately disclose returned-goods wastage already included in COGS.
6. Stock adjustment void timestamps exist, but current P&L removes every voided adjustment and only queries original `PerformedAt`. New economics must retain the original loss and emit a negative stock loss on `VoidedAt`, using the shop's half-open business-day window. Add a dedicated posted-in-range query rather than changing the history query's meaning.
7. `ExpenseRepository.GetByShopAndDateRangeAsync` already filters `!IsVoided` before returning all dated rows. Plan 29 remains pending for expense-list pagination/status and is not a prerequisite for report correctness. Do not base reports on the paginated list or implement unrelated filter work here.
8. `Expense.CreateFromSupplierPayment` provides authoritative provenance; linked historical records can be backfilled as supplier settlements. `CreateCorrection` currently loses the supplier link; backfill must follow same-shop correction ancestry, and future corrections must inherit immutable supplier-settlement treatment. Category names are not evidence. Plan 25's reversal already walks the correction chain and voids active descendants.
9. Expense corrections are replacement facts, not dated reversal events: original is marked void, replacement has its own `ExpenseDate`, and both months must be open. Preserve that explicit existing policy rather than inventing missing historical void dates. A closed original month remains protected. Expense classification changes use this same audited correction flow.
10. P&L exports and dashboard both consume `ProfitLossReportBuilder`. Excel currently renders null ratios as zero, and dashboard calls trading profit “Net Profit.” Update these user-facing consumers as part of this feature. Do not reinterpret cash/outflow charts as accrual expenses without changing their labels.

### Concrete definitions and compatibility

- `EconomicLine(NetRevenue, EconomicCost, GrossProfit, GrossMarginPercent?, MarkupPercent?)`: net revenue is frozen post-discount `TaxableAmount`; recorded goods cost is rounded `quantity * recordedCostPrice`; service recorded cost is zero with an explicit missing-cost flag. Profit = revenue − cost; ratios use revenue and cost respectively, each rounded to two decimals. Exactly zero denominators produce null; negative period denominators retain the signed arithmetic and must not be hidden.
- `DiscountCapacity = max(0, roundedNetBeforeDiscount − roundedRecordedCost)`. Output tax never adds capacity. Existing below-cost sales without discounts remain refused; no implicit manager exception.
- New report summary `Economics` contains `Version=2`, `CostBasis="recorded-sale-cost/1"`, `NetSalesExcludingTax`, `Cogs`, `StockLoss`, `ReturnedGoodsWastageIncludedInCogs`, `GrossTradingProfit`, `OperatingExpenses?`, `OperatingProfit?`, `GrossMarginPercent?`, `MarkupPercent?`, `UnclassifiedExpenseCount`, `UnclassifiedExpenseAmount`, `OperatingProfitStatus`, and `Limitations` (stable string codes).
- `GrossTradingProfit = NetSalesExcludingTax − Cogs − StockLoss`. Summary gross-margin and markup numerator is this trading profit; denominators are net sales and COGS respectively. Row figures use that row's signed values; no average of row percentages.
- `OperatingProfit = GrossTradingProfit − OperatingExpenses` only for the full period (type all, empty search), no active unclassified expenses, no unreviewed return accounting and no unmodeled service costs. Otherwise null with status `FilteredActivity`, `UnclassifiedExpenses`, `AccountingReview`, or `ServiceCostsUnmodeled` in that precedence, plus all applicable limitation codes. A genuine zero is displayed as zero; null is “Not available” with its reason.
- For filtered trading activity, leave `OperatingExpenses` null as well; never subtract the whole shop's expenses from a searched invoice. Counts/amounts of unclassified expenses may be returned for diagnostics but must be labeled period-wide. Pagination never changes summaries. Always disclose `purchase-tax-recoverability-unmodeled` and `historical-cost-snapshots-preserved`; these explain the recorded-cost basis, not an invented tax deduction.
- `ExpenseTreatment`: `Unclassified=0`, `Operating=1`, `SupplierSettlement=2`, `CapitalOrOtherExcluded=3`. New/legacy ordinary requests omitting treatment stay Unclassified. Operator selection is required to assert Operating/Excluded. SupplierSettlement is set only by provenance and cannot be chosen/spoofed by a generic expense request.
- Add optional fields at the end of existing request/DTO records to preserve existing JSON callers and source fixtures. Legacy P&L fields remain available with documented legacy meanings; UI/XLSX use the new fields. Add `Economics` to dashboard, retain `NetProfit` for old clients, and relabel its current card as gross trading profit.

## Review Focus

1. Purchase-inclusive cost and fractional quantity: no tax deduction without evidence; all cost/capacity arithmetic rounds at the same boundary (Tasks 1–2).
2. Frozen offline v1 invoices: new policy blocks issuance only, while legacy replay keeps the printed totals and idempotency and opens one review case (Task 2).
3. Supplier-payment correction ancestry, including multiple generations and cross-shop references: never deduct a settlement as operating expense (Task 3).
4. Sale, return, adjustment and void in different months: reverse each cost once in its owning period and preserve closed-month fences (Task 4).
5. Search/type filters, unclassified rows, service revenue, null ratios and Excel: do not present partial/incomplete amounts as full business operating profit (Tasks 4–6).

---

## Task 1: Shared recorded-cost economics and online discount protection

**Files:**
- Create `src/backend/Intelibill.Application/Features/Sales/Services/Pricing/SaleEconomicsCalculator.cs`.
- Modify `src/backend/Intelibill.Application/Features/Sales/Services/Pricing/SalePricingMath.cs`, `SalePricingCalculator.cs` and, only if necessary for diagnostic amounts, `ISalePricingCalculator.cs`.
- Modify `src/backend/Intelibill.Domain/Entities/InventoryBatch.cs`, preview/record handlers above, and `src/backend/Intelibill.Infrastructure/Repositories/InventoryBatchRepository.cs`.
- Modify `src/backend/Intelibill.Application/Common/Errors/Errors.cs` and `src/backend/Intelibill.Application/Features/Sales/DTOs/SalePreviewDto.cs` for additive economics/diagnostics.
- Tests: existing `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Sales/Services/SalePricingCalculatorTests.cs`, preview/record handler suites, domain InventoryBatch tests; new `SaleEconomicsCalculatorTests.cs` alongside calculator tests.

**Interfaces:**
- `SaleEconomicsCalculator.Calculate(decimal netRevenue, decimal recordedCost): EconomicLine`.
- `SaleEconomicsCalculator.RecordedCost(decimal quantity, decimal unitCost): decimal`.
- `SaleEconomicsCalculator.DiscountCapacity(decimal netBeforeDiscount, decimal recordedCost): decimal`.
- Add row `Economics` to successful `SalePreviewLineDto`; keep preview/post error behavior and existing stable codes. Below-cost error descriptions must state requested net revenue, recorded cost, and loss so a rejected preview can display the economics without a successful/postable preview DTO.

- [ ] Add failing tests: cost100/inclusive118/tax18 with flat10 discount rejects (`net90`, `profit-10` diagnostic), exclusive100/tax18 rejects identically; zero discount at net100 succeeds; cost100/inclusive141.60 accepts20, refuses20.01. A batch `CostPrice=118, PurchaseTaxIncluded=true, TaxRate=18` still supplies cost118, never100. Client-supplied cost cannot lower authoritative online cost.
- [ ] Add calculator ratio cases: `(200,100) => profit100, margin50, markup100`; `(0,0) => both null`; `(100,0) => margin100, markup null`; `RecordedCost(.333,100)=33.30`. Assert exact preview/posted economics for accepted inclusive/exclusive requests.
- [ ] Run focused unit tests red, then implement pure functions and remove inclusive-tax capacity logic. Replace tax-stripping cost behavior with explicitly documented recorded cost; update all three production callers consistently. Do not touch historical sale/return records.
- [ ] Add mixed-rate/fractional allocation fixture: line A qty.5, unit net200, recorded unit cost190 => net100/cost95; line B qty1.25, unit net80, recorded unit cost40 => net100/cost50; taxes18%/5%, sale discount20 => allocations5/15, taxable95/85, cost95/50, total discount20 exactly. Inclusive shelf equivalents236/84 produce identical results. Services remain excluded. Add equal-remainder .01 tie and full-capacity boundary cases.
- [ ] Run `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter 'FullyQualifiedName~SalePricingCalculatorTests|FullyQualifiedName~SaleEconomicsCalculatorTests|FullyQualifiedName~PreviewSaleQueryHandlerTests|FullyQualifiedName~RecordSaleCommandHandlerTests'` and affected domain tests; require pass. Parent commits this reviewed unit.

## Task 2: Offline issuance guard and honest checkout diagnostics

**Files:**
- Create `src/frontend/src/app/features/sales/services/offline-sale-economics.ts` and its spec.
- Modify `offline-sale-finalization.service.ts`, `offline-sale-state.service.ts`, `pages/new-sale-page.discount-validation.service.ts`, `pages/new-sale-page.offline-flow.service.ts`, and corresponding specs under `src/frontend/src/app/features/sales/`.
- Modify checkout `components/new-sale/cart-checkout-summary.component.{ts,html}`, sale preview/models/service tests as necessary to display successful economics and rejected-preview descriptions.
- Modify `src/backend/Intelibill.Application/Features/Sales/Commands/SyncOfflineSales/OfflineSaleSyncHelpers.cs` and `OfflineSalePoster.cs`; extend `SyncOfflineSalesCommandHandlerTests.cs`.
- Existing v1 calculator, arithmetic validator, shared v1 JSON fixture and queued payload/hash remain unchanged.

**Interfaces:**
- `evaluateOfflineSaleEconomics(pricing: OfflineFrozenSalePricing): { lines: readonly OfflineLineEconomics[]; belowCostLineIds: readonly string[] }`, using existing scaled bigint money/quantity helpers, not JS floating comparison.
- Add finalization refusal `BELOW_RECORDED_COST`; map to translated actionable copy. Preview shows the same net revenue/cost/profit basis and safe floored percentage limits (not rounded-up percentage limits).
- Add `AddEconomicVarianceWarnings(...)` near existing pricing warnings; creates existing `PricingVariance` issue with code `offline_sync.below_recorded_cost`, preserves issued amounts, and checks each frozen goods line's cost/revenue. Also surface current-vs-frozen cost differences as provenance variance, never silently rewrite cost.

- [ ] Write failing finalization test: net90/cost100 produces refusal before lease consumption, queue save and shadow reduction; net100 succeeds. Cost is taken from refreshed usable snapshot, not forged UI input. Add fractional paise and service-zero-cost tests.
- [ ] Write preview tests asserting safe capacity/percentage, informative rejection amounts, and no stale successful economics after a failed/scope-stale preview. Respect existing preview request identity and shop transition clearing.
- [ ] Implement policy guard around unchanged `calculateOfflineFrozenSale`; do not clamp an issued invoice, silently reduce discounts, or change its v1 wire values. If capped online allocation would be needed, show the supported offline refusal with an instruction to reduce discount or finish online.
- [ ] Add backend test for already-issued v1 below-cost sale: frozen amounts and hash unchanged; sale posts with a pricing review case; retry yields existing sale/case without duplicates. Valid v1 invoices still pass the pinned arithmetic fixture.
- [ ] Run targeted frontend finalization/state/preview/checkout specs and backend sync tests red→green. Run existing offline arithmetic contract tests unchanged. Parent commits this reviewed unit.

## Task 3: Provenance-safe expense classification and migration

**Files:**
- Create `src/backend/Intelibill.Domain/Enums/ExpenseTreatment.cs`.
- Modify `src/backend/Intelibill.Domain/Entities/Expense.cs`, `src/backend/Intelibill.Infrastructure/Data/Configurations/ExpenseConfiguration.cs`.
- Modify `src/backend/Intelibill.Api/Controllers/ExpensesController.cs`; RecordExpense and CorrectExpense command, validator and handler files; `Features/Expenses/DTOs/ExpenseDto.cs`, `ExpenseListItemDto.cs` and their query mappers.
- Create EF migration `AddExpenseTreatment` and generated designer/model snapshot under Infrastructure/Migrations; document/backfill SQL in `scripts/db/backfill-expense-treatment.sql` if separated from migration.
- Tests: domain Expense tests, existing RecordExpense/CorrectExpense handler and controller tests, `tests/backend/integration/Intelibill.Integration.Tests/ExpensesControllerTests.cs`.

**Interfaces:**
- `Expense.Treatment` immutable except through replacement correction; `Create(..., ExpenseTreatment treatment = Unclassified)`; supplier factory always stamps SupplierSettlement.
- Generic record request `ExpenseTreatment Treatment = Unclassified`. Correction request `ExpenseTreatment? Treatment = null`: omission inherits original treatment. For a supplier settlement, explicit differing treatment is refused; correction retains supplier provenance/treatment. Unknown enum values or direct SupplierSettlement requests answer validation errors.
- Persist treatment as bounded enum/string using repository conventions with safe default Unclassified; DB check constraint for allowed values. No new table/RLS policy required.

- [ ] Add failing tests: ordinary unclassified default, explicit operating, excluded capital, rejection of forged settlement, correction inherits classification, supplier settlement cannot become Operating, two-generation supplier corrections still excluded, reversal voids their active descendants.
- [ ] Implement factories/request validation/mappers. Existing owner/manager restriction and both-month posting fence remain. Use corrected expense chain to revise classification instead of an unaudited mutable category rule.
- [ ] Generate migration. Backfill only roots with `SupplierLedgerEntryId IS NOT NULL` and same-shop descendants through `OriginalExpenseId`; use a recursive CTE with cycle-safe UNION/deduplication, never cross shops. All other records remain Unclassified, even if category text says “Supplier Payments” or “Rent.” The script is idempotent and must not overwrite an operator's explicit later non-supplier classification except where authoritative supplier provenance requires settlement.
- [ ] Test migration against pre-migration rows with linked root, two corrections, unrelated lookalike category, cycle, and cross-shop references; assert safe defaults and persisted enums after context reload. Re-run backfill to prove idempotence. Use dedicated disposable database, never mutate the developer's data for this fixture.
- [ ] Run affected unit and Docker expense/payment integration tests red→green. Parent commits migration and classification together.

## Task 4: Versioned report economics and dated stock-loss reversals

**Files:**
- Create `src/backend/Intelibill.Application/Features/Sales/DTOs/ProfitLossEconomicsDto.cs` (summary and row records).
- Modify `Features/Sales/DTOs/ProfitLossReportItemDto.cs`, `ProfitLossReportResultDto.cs`, `Queries/GetProfitLossReport/ProfitLossReportBuilder.cs`.
- Modify `src/backend/Intelibill.Domain/Interfaces/Repositories/IInventoryAdjustmentRepository.cs`, `src/backend/Intelibill.Infrastructure/Repositories/InventoryAdjustmentRepository.cs`; use existing `IExpenseRepository.GetByShopAndDateRangeAsync`.
- Update constructor fixtures/DI consumers including dashboard and export handlers. Extend existing `GetProfitLossReportQueryHandlerTests.cs`; add `ProfitEconomicsTests.cs` integration fixture.

**Interfaces:**
- `IInventoryAdjustmentRepository.GetPostedInRangeAsync(Guid shopId, UtcDateWindow window, CancellationToken ct): Task<IReadOnlyList<InventoryAdjustment>>`, selecting original OR void instant in range with shop predicate, no `!IsVoided` filter.
- `ProfitLossEconomicsDto` follows the exact summary properties/definitions above. `ProfitLossRowEconomicsDto` includes Version, NetSalesExcludingTax, Cogs, StockLoss, ReturnedGoodsWastageIncludedInCogs, GrossTradingProfit, GrossMarginPercent?, MarkupPercent?, HasUnmodeledServiceCost. Append nullable `Economics` property to existing DTO constructors where source compatibility helps; builder always supplies it.
- Builder consumes same calculator as pricing, expense repository, return reader and posted adjustments. Use `ReturnAdjustmentEntry` signs directly—its void quantities/adjustments are already negative. Unknown return tax is flagged, never reconstructed.

- [ ] Add fixture net200/cost100, operating expense20, supplier payment100, excluded equipment50 => net sales200, COGS100, loss0, trading100, OpEx20, operating80, margin50, markup100. Mark ordinary expense Unclassified => trading100 stays visible, operating null and unclassified amount20/count1.
- [ ] Add empty range/zero-cost/zero-net denominator tests and negative return-only month tests. Add service revenue50 with cost zero => trading includes recorded revenue but service limitation and operating unavailable. Add accounting-review return => explicit incomplete state.
- [ ] Add return fixture: sale200/cost100; full restockable refund reverses revenue200 and COGS100 once; full wastage refund reverses revenue200 with COGS unchanged, diagnostic wastage100 and incremental StockLoss0. Return void reverses its signed postings exactly in the void month. Partial zero/partial-refund cases use posted snapshot adjustments, not price/tax recomputation.
- [ ] Add independent loss10 in June, void in July => June StockLoss10, July StockLoss−10, combined0. Query includes a June original when only July void is requested. Test IST midnight boundary. Preserve existing adjustment-void posting fences; no new cross-period write policy.
- [ ] Add expense correction20→30 on separate open dates => original voided excluded, replacement30 included only on its own date; original closed month refuses correction. Supplier correction/reversal never changes OpEx. Later receipt cost correction must not rewrite frozen COGS; limitation notes it is not a revaluation model.
- [ ] Add filtered report tests: search/type affects all row aggregates before pagination; operating and OpEx null with FilteredActivity; paging alone keeps full-period economics. Tenant-B expenses and adjustments never affect tenant-A report. Implement and run focused report tests red→green, then Docker `ProfitEconomicsTests`/`ProfitLossControllerTests`.
- [ ] Keep legacy margin fields' cost denominator and existing legacy profit naming/formulas documented. Dated loss-reversal rows fix prior missing history; expose their `IsReversal` marker. Never map new margin values into old `MarginPercent`. Parent commits this reviewed unit.

## Task 5: One economics projection for exports and dashboard

**Files:**
- Modify `src/backend/Intelibill.Application/Features/Exports/ProfitLoss/DTOs/ProfitLossExport{Dataset,Summary,Row,Metadata}Dto.cs`, `Queries/ExportProfitLoss/ExportProfitLossQueryHandler.cs`.
- Modify `src/backend/Intelibill.Infrastructure/Services/Exports/ProfitLossExcelExportRenderer.cs`.
- Modify dashboard `DTOs/DashboardDto.cs`, `Queries/GetDashboard/GetDashboardQueryHandler.cs`, frontend dashboard model/metric mapper/card and their current tests.
- Tests: existing `ProfitLossExcelExportRendererTests.cs`, `ExportProfitLossQueryHandlerTests.cs`, `GetDashboardQueryHandlerTests.cs` and report/API integration fixture.

**Interfaces:**
- Export dataset embeds the same versioned row/summary economics records without recomputation; metadata states version, recorded-cost basis, business timezone and limitations. Workbook uses explicit economic labels.
- Dashboard adds the same period `Economics` summary; old `NetProfit` remains for old clients. Current web card reads `GrossTradingProfit` and is labeled “Gross trading profit”; show completeness/service limitations and expose operating profit only with its status. Existing all-expense outflow series stays explicitly labeled as recorded expenses/outflows, not operating costs.

- [ ] Write exact export assertions for fixture200/100/20: workbook net sales200, COGS100, trading100, OpEx20, operating80, margin50, markup100. Assert “Gross margin %” and “Markup %” columns, no misleading “Net Profit After Tax,” and null ratios/operating values render “Not available” or blank with a reason, never zero.
- [ ] Add workbook row flags/notes for reversal and NeedsAccountingReview. Return/service/classification limitations and filtered scope must survive export mapping. Date cells use declared shop business timezone, not unlabeled UTC if displaying local dates.
- [ ] Implement projection reuse, preserve formula-looking names as literal XLSX text using existing ClosedXML behavior, and extend tests for an unclassified row and a searched single invoice.
- [ ] Add dashboard test matching report version2 totals for the same range; no extra subtraction of raw `expenseTotal`. Test previous-period comparison against the same named metric. Run export/API/dashboard focused tests red→green. Parent commits this reviewed unit.

## Task 6: Expense choices and honest web report/checkout labels

**Files:**
- Modify `src/frontend/src/app/features/expenses/services/expense.service.ts`, record/correct expense overlay TS/HTML/specs, expense table TS/HTML, and affected NgRx DTO plumbing.
- Modify `src/frontend/src/app/features/sales/services/sale.models.ts`, `pages/profit-loss-page/profit-loss-page.component.{ts,html,scss,spec.ts}` and affected sales state fixtures.
- Modify dashboard files from Task 5, checkout summary/preview files from Task 2 as needed.
- Update all locale JSON files under `src/frontend/public/assets/i18n/` with consistent keys; no hardcoded English fallback as final UI.

- [ ] Write record/correction overlay tests: user explicitly chooses Operating or Capital/Other excluded; omission stays Unclassified; supplier settlement classification is read-only with provenance wording; correction sends chosen classification and preserves existing reason/description/date flow.
- [ ] Write report rendering tests for all eight named figures, margin50/markup100, zero vs null, unclassified and service warnings, and search/type suppression of operating totals. Return wastage says “already included in cost,” not an additional expense.
- [ ] Implement typed additive contracts and UI labels. Explain recorded-cost basis succinctly: “Uses recorded goods costs. Purchase-tax recovery and service costs are not verified.” Label incomplete operating profit with its reason and link to Expenses where appropriate. Do not claim audited net profit.
- [ ] Show rejected inclusive-tax preview net90/cost100/loss10 through the existing error path; accepted preview values must agree with posted figures. Preserve scope guards and request identity behavior. New offline refusal must be actionable and translated.
- [ ] Run targeted frontend specs using `bun run test --run <spec paths>` from `src/frontend`; run `bun run build`. Verify all locale keys parse and resolve. Parent commits this reviewed unit.

## Task 7: End-to-end evidence, rollout and documentation

**Files:**
- New `tests/backend/integration/Intelibill.Integration.Tests/ProfitEconomicsTests.cs`; extend existing restricted-role RLS fixture where tenant isolation is tested.
- Modify `docs/backend-architecture.md`, `docs/frontend-architecture.md`, `AGENTS.md` only with accurate changed contracts; retain source spec as provenance and mark completion through normal repository backlog convention after verification.

- [ ] Drive real API fixture: inventory with purchase-inclusive recorded cost; preview/record accepted and rejected discounts; ordinary classified expense; supplier payment→expense correction→payment reversal; return/restock/wastage; independent loss and later void. Assert API report and generated workbook numbers and completeness agree after each step.
- [ ] Verify migration applies from previous schema and fresh schema, unknown treatment is rejected, old clients omitting treatment remain accepted as Unclassified, legacy API metric values retain documented formulas, old offline v1 fixture/hash and invoice document snapshot are unchanged. Use restricted DB role for tenant-crossing fixture.
- [ ] Run `dotnet build src/backend/Intelibill.slnx`, `dotnet test src/backend/Intelibill.slnx` with Docker available. From `src/frontend`, run `bun run test`, `bun run build`, then `bun run audit:ui:release` because the production worker packaging contract must remain intact. Capture actual counts/results; do not describe unexecuted checks as passed.
- [ ] Review diff for unintended generated changes, source-linked classification, null handling, tenant predicates, period boundaries, arithmetic v1 drift and legacy field semantic changes. Run independent whole-branch review/fix loop as requested by user; rerun affected checks after fixes.
- [ ] Document rollout: deploy migration/API additive fields first; backfill linked settlement ancestry, expose remaining unclassified totals for operator review, then web. Compare a representative closed month; expected differences are labels, explicit limitations and corrected dated stock-loss history—not rewritten sale invoices. Rollback may retain additive columns and old readers but must not restore unsafe tax-inclusive discount checks. Parent creates PR with exact validation and remaining limitations.

## Deliberately bounded policies and future decisions

Purchase-tax recoverability, attribution of receipt cost-correction deltas to sold/unsold inventory, and modeled service costs require separate accounting evidence/policy. This implementation publishes recorded-cost metrics with those limits and never invents deductions or historical corrections. Below-cost override is absent and stays absent. Expense categorization is explicit per expense, not an inferred rule by category name. Offline v1 arithmetic stays stable; accepting more allocation patterns offline would require a separately versioned arithmetic contract.

## Plan self-review

Coverage checked: inclusive/exclusive guards and purchase-tax assumption (Task1), fractional capacity conservation (Task1), frozen offline issuance/replay (Task2), supplier correction ancestry and safe backfill (Task3), return/loss/date semantics and incomplete operating figures (Task4), API compatibility/dashboard/XLSX (Task5), expense choices and all UI labels (Task6), migration/RLS/full verification/PR evidence (Task7). All five Review Focus cases have named acceptance fixtures. No product code or test execution is claimed by this planning document. The remaining accounting-policy limits are exposed behavior, not hidden implementation TODOs.

## Execution and verification — 7 November 2026

Astra completed current-source analysis and planning; Sol implemented Tasks 1–7. Independent Astra review found four behavior defects and one acceptance-evidence gap. All were addressed in `e7dfc67d`: conserved dated partial-return costs and exact void/re-return allocations; exact scaled-money discount ceilings; preservation of explicit dashboard null comparisons; numeric search over displayed economics; and a real API/PostgreSQL issued-v1 below-cost replay/retry fixture preserving one sale, one economic case, the payload hash and issued document.

Final product-code verification passed: backend build with zero warnings/errors; 389 Domain, 1699 Application, 578 API and 681 PostgreSQL integration tests (3347 total); 2463 frontend tests plus 59 script tests; production frontend build; and all 11 release UI checks. The final integration-only run used `dotnet test tests/backend/integration/Intelibill.Integration.Tests --no-build --no-restore` against the same compiled product code as the passing full unit suites. No findings are deferred.

Return-cost residuals are allocated cumulatively from the original rounded sale-line cost over the full tenant-scoped dated history. Voids reverse their original allocation exactly and release quantity/cost for later returns; report windows never redistribute earlier paise. Legacy DTO formulas and stored snapshots remain unchanged. The legacy `GetProfitCostPrice` method name is retained for source compatibility while returning recorded cost. The migration and rerunnable backfill set and restore shop context explicitly because FORCE RLS applies to table owners too.

Rollout retains the bounded policies above: apply the expense-treatment migration before web deployment; ordinary historical expenses remain Unclassified; purchase-tax recoverability, receipt-cost revaluation, and service costs are not invented; unsafe new offline v1 allocations are refused without changing issued arithmetic; mobile is excluded.

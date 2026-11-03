# Balanced Return Accounting Exports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every sale-return export (Tally XML, XLSX, PDF) and the P&L report read one persisted, versioned return accounting snapshot whose debits equal its credits, while the original returned merchandise value stays separately auditable.

**Architecture:** A return is posted with an immutable accounting snapshot: per-line taxable/tax/non-tax adjustment (computed by a versioned `IReturnAccountingPolicy`) plus explicit settlement legs (`sale_return_settlements`). A pure projector turns returns and their voids into dated adjustment entries (return in its own period, void as a linked reversal in its own period); exports and P&L consume only those entries. Tally renders through `IAccountingVoucherValidator` and refuses, with per-record diagnostics, rather than emit an unbalanced or unreviewed voucher.

**Tech Stack:** ASP.NET Core 10, C#, ErrorOr, EF Core 10/Npgsql, PostgreSQL (RLS), ClosedXML, QuestPDF, xUnit + Testcontainers.

**Spec:** `pending-tasks/17-balanced-return-accounting-exports-done.md` (local, git-ignored), market-readiness review `docs/reviews/2026-10-21-market-readiness.md`.

## Global Constraints

- Domain ← Application ← Infrastructure ← Api; ErrorOr results, no exception control flow; warnings are errors; no data annotations on entities; EF mappings via `IEntityTypeConfiguration<T>`.
- Decimal money, two places, `MidpointRounding.AwayFromZero` (PostgreSQL `round(numeric, 2)` matches).
- New tenant table gets ENABLE + FORCE RLS with the `shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid` policy, a `TenantPolicyManifest` entry and a row in `docs/security/tenant-policy-matrix.md`.
- Posted facts are immutable: a snapshot is posted once; a void is a reversal entry, never an edit.
- Tax/document legality belongs to plan 20. This plan ships one declared arithmetic policy (`proportional-line-tax/1`) and never lets a renderer invent a treatment.
- No AI attribution in commits or PR.

## Decisions (made here; flagged for accountant/user confirmation in the PR)

| # | Decision | Why |
|---|---|---|
| D1 | Policy `proportional-line-tax/1`: per line, `within = min(approved, shareGross)`; `tax = round(within × shareTax / shareGross)`; `taxable = within − tax`; `nonTax = approved − within`. `approved = 0` → all zero. `shareGross ≤ 0` with `approved > 0`, or `shareTax ∉ [0, shareGross]` → unsupported. | Matches what P&L already did for partial refunds, so history does not shift; never reverses more tax than the invoice share charged. ₹50 of a ₹118 (₹100 + 18%) share → ₹42.37 + ₹7.63. |
| D2 | Over-refund excess (approved above the discounted paid share) is a commercial-only `NonTaxAdjustment`, posted to ledger `Sales Return Adjustments` (Indirect Expenses). | The snapshot needs a non-tax leg; reducing output tax beyond what was charged is not supportable. |
| D3 | Unsupported policy input refuses the return at posting (400 `SaleReturn.AccountingPolicyUnsupported`); preview warns `sale_return.accounting_unsupported`. | "Blocks posting explicitly": no return is recorded without a balanced snapshot from now on. |
| D4 | Settlement kinds: `ReceivableReduction` (customer ledger, `customer_id`, `customer_ledger_entry_id`), `StoreCreditIssued` (`credit_note_id`, ledger `Credit Note Liability`), `ExternalRefund` (ledger `Customer Refunds Clearing`, Current Liabilities). | Refund tender is not recorded today (plan 25 owns payout evidence); a clearing ledger does not infer cash/UPI. |
| D5 | Returns post on `ProcessedAt` (UTC date), voids post a reversal on `VoidedAt`; both are included when their own date is in range. | Plan 27 swaps UTC for business date later; lifetime-sale attachment is removed. |
| D6 | Tally export answers 409 with one error per blocked record (`Export.ReturnNeedsAccountingReview`, `Export.VoucherUnbalanced`) instead of emitting a partial or plugged file. XLSX/PDF still render and show the status. | "Refuse invalid vouchers with actionable diagnostics rather than a hidden balancing plug." |
| D7 | Legacy backfill applies D1 to every computable legacy line (policy recorded as `backfill:proportional-line-tax/1`); a return is `NEEDS_REVIEW` when its payout settlement is not evidenced (null destination, or credit-note destination without a matching note) or a line is policy-unsupported. | Settlement is never inferred; tax split is deterministic arithmetic already shown in P&L. |
| D8 | Repeated returns allocate the invoice share cumulatively: `share = round(original × cumulativeQty / soldQty) − alreadyAllocated` for gross, taxable and tax separately. | The final return exhausts each original component exactly once. |

## Review Focus

1. A return voided in a later period: the earlier export must still show the credit note; the later one shows only the reversal → Task 6 `Void_ProducesLinkedReversalInItsOwnPeriod`.
2. A refund above the paid share (allowed with a note today) → Task 2 `OverRefund_ExcessIsNonTaxAdjustment` and Task 5 voucher balance.
3. Walk-in sale with due reduced by a return (no customer id) → Task 3 `RecordReturn_WalkInDueReduction_PostsReceivableWithoutCustomer`.
4. Credit-note destination selected but payout is zero (all due) → Task 3 `RecordReturn_AllDue_PostsSingleReceivableLeg`.
5. Legacy return whose payout destination is null → Task 6 `UnbalancedLegacySnapshot_ProducesReviewDiagnostic`.

---

### Task 1: Domain snapshot, settlements and persistence

**Files:**
- Create: `src/backend/Intelibill.Domain/Enums/ReturnAccountingStatus.cs` (`Balanced = 1`, `NeedsReview = 2`)
- Create: `src/backend/Intelibill.Domain/Enums/ReturnSettlementKind.cs` (`ReceivableReduction = 1`, `ExternalRefund = 2`, `StoreCreditIssued = 3`)
- Create: `src/backend/Intelibill.Domain/ValueObjects/ReturnAccountingSnapshot.cs`
- Create: `src/backend/Intelibill.Domain/Entities/SaleReturnSettlement.cs`
- Modify: `src/backend/Intelibill.Domain/Entities/SaleReturn.cs`, `SaleReturnItem.cs`
- Create: `src/backend/Intelibill.Infrastructure/Data/Configurations/SaleReturnSettlementConfiguration.cs`; modify `SaleReturnConfiguration.cs`, `SaleReturnItemConfiguration.cs`, `ApplicationDbContext.cs`, `TenantPolicyManifest.cs`
- Test: `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/SaleReturnAccountingTests.cs`

**Interfaces (produces):**
- `record ReturnLineAccounting(Guid SaleItemId, decimal TaxableAmount, decimal TaxAmount, decimal NonTaxAmount)` with `Total`.
- `record ReturnSettlementInput(ReturnSettlementKind Kind, decimal Amount, Guid? CustomerId = null, Guid? CreditNoteId = null, Guid? CustomerLedgerEntryId = null)`.
- `ReturnAccountingSnapshot.Create(string policyVersion, IReadOnlyList<ReturnLineAccounting> lines, IReadOnlyList<ReturnSettlementInput> settlements) : ErrorOr<ReturnAccountingSnapshot>`; exposes `PolicyVersion`, `Lines`, `Settlements`, `TaxableAmount`, `TaxAmount`, `NonTaxAmount`, `FinancialAdjustment`, `SettledAmount`.
- `SaleReturn.PostAccounting(ReturnAccountingSnapshot snapshot) : ErrorOr<Success>`; new properties `AccountingStatus?`, `AccountingPolicyVersion?`, `AccountingReviewReason?`, `AdjustmentTaxableAmount?`, `AdjustmentTaxAmount?`, `AdjustmentNonTaxAmount?`, `Settlements`, computed `OriginalReturnedGross` (Σ item `MaxRefundAmount`).
- `SaleReturnItem` new `AdjustmentTaxableAmount?`, `AdjustmentTaxAmount?`, `AdjustmentNonTaxAmount?`.
- `SaleReturnSettlement { ShopId, SaleReturnId, Sequence, Kind, Amount, CustomerId?, CreditNoteId?, CustomerLedgerEntryId? }`.

- [ ] Step 1: tests — `PostAccounting_BalancedSnapshot_SetsStatusAndLineSplit` (₹50 line → 42.37/7.63, status Balanced, one ExternalRefund settlement of 50); `PostAccounting_SettlementSumMismatch_Refused` (`SaleReturn.AccountingSettlementMismatch`); `PostAccounting_LineDoesNotMatchApprovedRefund_Refused` (`SaleReturn.AccountingLineMismatch`); `PostAccounting_ReceivableLegMustEqualDueReduction`; `PostAccounting_StoreCreditRequiresCreditNoteDestination`; `PostAccounting_Twice_Refused` (`SaleReturn.AccountingAlreadyPosted`); `PostAccounting_Voided_Refused`; `Snapshot_NegativeAmount_Refused`.
- [ ] Step 2: run `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter FullyQualifiedName~SaleReturnAccountingTests` → compile failure.
- [ ] Step 3: implement. Enum columns stored as upper-snake strings (`BALANCED`, `NEEDS_REVIEW`, `RECEIVABLE_REDUCTION`, `EXTERNAL_REFUND`, `STORE_CREDIT_ISSUED`) like `purchase_receipt_values`. Checks: `ck_sale_returns_accounting_balanced` (BALANCED ⇒ policy and all three totals non-null and `taxable + tax + non_tax = total_refund_amount`), `ck_sale_return_items_adjustment_split` (all null, or all ≥ 0 and sum = `approved_refund_amount`), `ck_sale_return_settlements_amount_positive`, `ck_sale_return_settlements_references_by_kind` (store credit ⇔ `credit_note_id` not null). Settlement FKs: sale return cascade, credit note restrict, ledger entry restrict, customer restrict, shop cascade; unique `(sale_return_id, sequence)`; index `(shop_id, sale_return_id)`.
- [ ] Step 4: domain tests pass.

### Task 2: Policy and cumulative share allocation

**Files:**
- Create: `src/backend/Intelibill.Application/Features/Sales/Services/Returns/IReturnAccountingPolicy.cs`, `ProportionalLineTaxReturnAccountingPolicy.cs`
- Modify: `ISaleReturnCalculator.cs` (line request gains `PreviouslyReturnedQuantity`, `PreviouslyAllocatedTotalAmount`, `PreviouslyAllocatedTaxableAmount`, `PreviouslyAllocatedTaxAmount`, all default 0), `SaleReturnCalculator.cs`, `SaleReturnValidator.cs` (passes active-return sums per sale item), `DependencyInjection.cs`, `Errors.Sale.cs`
- Test: `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Sales/Services/ProportionalLineTaxReturnAccountingPolicyTests.cs`, `SaleReturnCalculatorTests.cs`

**Interfaces (produces):**
- `record ReturnAccountingLineRequest(Guid SaleItemId, decimal ShareGrossAmount, decimal ShareTaxAmount, decimal ApprovedRefundAmount)`.
- `interface IReturnAccountingPolicy { string Version { get; } ErrorOr<IReadOnlyList<ReturnLineAccounting>> Classify(IReadOnlyList<ReturnAccountingLineRequest> lines); }`
- `ProportionalLineTaxReturnAccountingPolicy.PolicyVersion = "proportional-line-tax/1"`.
- `Errors.Sale.ReturnAccountingPolicyUnsupported(Guid saleItemId)` → Validation `SaleReturn.AccountingPolicyUnsupported`.

- [ ] Step 1: tests — `PartialRefund_SplitsProportionally` (118/18/50 → 42.37/7.63/0), `FullRefund_ReturnsShareTaxExactly`, `ZeroRefund_AllZero`, `OverRefund_ExcessIsNonTaxAdjustment` (share 118/18, approved 130 → 100/18/12), `ZeroValueShareWithRefund_Unsupported`, `TaxAboveShare_Unsupported`; calculator `RepeatedFractionalReturns_AllocateFinalPaiseResidual` (sold 3, total 100, taxable 84.75, tax 15.25: returns of 1,1,1 → gross 33.33/33.34/33.33, tax 5.08/5.09/5.08, taxable 28.25×3) and `FinalReturn_AfterLegacyOverAllocation_ClampsAtZero`.
- [ ] Step 2: run `--filter "FullyQualifiedName~ProportionalLineTax|FullyQualifiedName~SaleReturnCalculatorTests"` → fail.
- [ ] Step 3: implement D1 and D8 (allocation clamped to `[0, original − allocated]`).
- [ ] Step 4: pass.

### Task 3: Post the snapshot when recording a return

**Files:**
- Modify: `RecordSaleReturnCommandHandler.cs` (classify right after warning validation, before any stock change; build settlements after the ledger entry and credit note exist; `PostAccounting` before `AddAsync`), `PreviewSaleReturnQueryHandler.cs` (warning), `ISaleReturnRepository.cs` + `SaleReturnRepository.cs` (include `Settlements`; add `GetPostedInRangeAsync(shopId, start, end)` = processed **or** voided in range, with items and settlements)
- Test: `RecordSaleReturnCommandHandlerTests.cs`

- [ ] Step 1: tests — `RecordReturn_PostsBalancedSnapshot_WithExternalRefundLeg`, `RecordReturn_MixedDueAndCreditNote_PostsBothLegsWithReferences`, `RecordReturn_AllDue_PostsSingleReceivableLeg`, `RecordReturn_WalkInDueReduction_PostsReceivableWithoutCustomer`, `RecordReturn_UnsupportedPolicy_RefusedWithoutSaving`.
- [ ] Step 2–4: fail → implement → pass (`dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter FullyQualifiedName~RecordSaleReturn`).

### Task 4: Shared adjustment projection, voucher model and validator

**Files:**
- Create in `src/backend/Intelibill.Application/Features/Exports/Accounting/`: `ReturnAdjustmentEntry.cs` (entry + `ReturnAdjustmentLine`, `ReturnAdjustmentTaxComponent`, `ReturnAdjustmentSettlement`, `ReturnAdjustmentEntryKind { Return, VoidReversal }`), `ReturnAdjustmentProjector.cs` (static, pure), `AccountingVoucher.cs`, `IAccountingVoucherValidator.cs`, `AccountingVoucherValidator.cs`, `AccountingLedgers.cs`
- Modify: `Errors.Export.cs`
- Test: `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Exports/Accounting/{ReturnAdjustmentProjectorTests,AccountingVoucherValidatorTests}.cs`

**Interfaces (produces):**
- `ReturnAdjustmentProjector.Project(IEnumerable<SaleReturn> returns, IReadOnlyDictionary<Guid, Sale> salesById, IReadOnlyDictionary<Guid, CreditNote> creditNotesById, DateOnly from, DateOnly to) : IReadOnlyList<ReturnAdjustmentEntry>` — amounts signed (reversal negative), entries ordered by posting time.
- Entry fields: `EntryKind, PostingDate (DateTimeOffset), SaleReturnId, ReturnNumber, SaleId, InvoiceNumber, CustomerName, Status (ReturnAccountingStatus), PolicyVersion?, ReviewReason?, OriginalReturnedGross, FinancialAdjustment, TaxableAdjustment?, TaxAdjustment?, NonTaxAdjustment?, Lines, TaxComponents, Settlements, VoidReason?`; `IsVoucherReady` (Balanced and financial ≠ 0).
- `record AccountingVoucher(string Number, string Type, DateOnly Date, string Reference, string? Narration, IReadOnlyList<AccountingVoucherLine> Lines)`; `record AccountingVoucherLine(string Ledger, decimal Amount, bool IsDebit)`.
- `IAccountingVoucherValidator.Validate(AccountingVoucher voucher) : ErrorOr<Success>` — ≥ 2 lines, every amount > 0 with ≤ 2 decimals, named ledgers, Σ debit = Σ credit exactly; error `Export.VoucherUnbalanced` naming voucher and both sums.
- `Errors.Export.ReturnNeedsAccountingReview(string returnNumber, string invoiceNumber, string reason)` and `Errors.Export.VoucherUnbalanced(...)` — Conflict.

- [ ] Steps: tests `Project_ReturnAndVoidInDifferentPeriods_EmitsEachInItsOwnRange`, `Project_LegacyWithoutSnapshot_NeedsReview`, `Project_TaxComponentsGroupByRate`, `Project_SettlementCarriesCreditNoteCode`; validator `Balanced_Passes`, `Unbalanced_ReportsBothSums`, `ZeroOrFractionalPaiseLine_Refused` → fail → implement → pass.

### Task 5: Exports and P&L consume the projection

**Files:**
- Modify: `SalesExportDatasetDto.cs` (replace `ReturnRows` with `ReturnAdjustments`), `SalesExportMetadataDto.cs` (`AccountingExportVersion = "2"`), delete `SalesExportReturnRowDto.cs`/`SalesExportReturnTaxBreakupDto.cs`, `SalesExportDatasetBuilder.cs` (projection by posting date; summary-row return columns = that sale's entries in range; tax breakup return columns from entries), `ISalesTallyXmlExportRenderer.cs` (returns `Task<ErrorOr<SalesExportResult>>`), `SalesTallyXmlExportRenderer.cs`, `TallyVoucherBuilder.cs` (build `AccountingVoucher`s → validate → XML), `TallyLedgerBuilder.cs`, `SalesExcel*Renderer.cs` (new `Return Adjustments` sheet; summary sheet reconciliation row for returns on sales outside the period), `SalesPdfDocument.cs` (adjustments table + metrics from entries), `ProfitLossReportBuilder.cs` (rows from entries; reversal rows; no tax inference), `ProfitLossReportItemDto` (+ `IsReversal`, `AccountingStatus`), `ExportSalesQueryHandler.cs`
- Test: `SalesTallyXmlExportRendererTests.cs` (parse every voucher and sum signed lines), `SalesExcelExportRendererTests.cs`, `SalesPdfExportRendererTests.cs`, `SalesExportDatasetBuilderTests.cs`, `GetProfitLossReportQueryHandlerTests.cs`, `ExportSalesQueryHandlerTests.cs`

- [ ] Steps: tests `Tally_EveryVoucherBalances_IncludingMixedAndPartial`, `Tally_ZeroRefund_EmitsNoReturnVoucher`, `Tally_MixedSettlement_CreditsEachLegSeparately` (₹40 customer, ₹60 Credit Note Liability), `Tally_VoidReversal_SwapsSidesAndReferencesOriginal`, `Tally_NeedsReview_RefusedWithDiagnostic`, `Dataset_ReturnPostsInItsOwnPeriod`, `Excel_ReturnAdjustmentsSheetTotalsMatchTaxBreakup`, `ProfitLoss_UsesSnapshotRevenueNotProratedTax`, `ProfitLoss_VoidInLaterPeriod_AddsReversalRow` → fail → implement → pass; then full unit suites.

### Task 6: Migration, legacy backfill, end-to-end reconciliation, docs

**Files:**
- Create: migration `20261103120000_AddReturnAccountingSnapshots` (+ Designer, snapshot update), `scripts/db/backfill-sale-return-accounting.sql` (same idempotent statement as the migration's backfill)
- Create: `tests/backend/integration/Intelibill.Integration.Tests/ReturnAccountingExportTests.cs`
- Modify: `docs/backend-architecture.md` (§ Return Accounting Exports), `docs/security/tenant-policy-matrix.md`, `AGENTS.md`

- [ ] Step 1: integration tests (real API + PostgreSQL): `PartialRefund_UsesApprovedAdjustment`, `MixedDueAndStoreCredit_PreservesBothLegs`, `ZeroRefund_HasNoFinancialVoucherButPreservesStockFact`, `RepeatedFractionalReturns_AllocateFinalPaiseResidual`, `UnsupportedTaxAdjustment_BlocksPostingOrExportExplicitly`, `UnbalancedLegacySnapshot_ProducesReviewDiagnostic` (backfill run twice; second affects 0 rows), `LegacyEvidencedReturn_BackfillsBalanced`, `Void_ProducesLinkedReversalInItsOwnPeriod`, `AllFormats_AgreeOnAdjustmentTotals` (Tally Σ return vouchers = XLSX adjustments total = P&L return revenue incl. tax).
- [ ] Step 2: `dotnet ef migrations add AddReturnAccountingSnapshots`, then hand-add RLS SQL and backfill SQL.
- [ ] Step 3: `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter "FullyQualifiedName~ReturnAccountingExportTests|FullyQualifiedName~TenantPolicyManifest|FullyQualifiedName~SalesExports|FullyQualifiedName~SaleReturn"` → pass.
- [ ] Step 4: full build + all suites; update counts in `AGENTS.md`/architecture doc.

## Migration, rollout and rollback

Columns are nullable; the migration backfills every existing return (D7) and the script re-runs idempotently after rollout for returns an old API instance wrote meanwhile (old instances post no snapshot; the projector reports them as needing review until the script runs). Rollback: keep snapshots readable and refuse Tally return export; never restore the unbalanced voucher builder.

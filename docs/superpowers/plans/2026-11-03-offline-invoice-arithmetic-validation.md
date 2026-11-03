# Offline Invoice Arithmetic Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reject inconsistent frozen offline invoices before any sale, stock or customer-ledger posting, without repricing historical bills against today's catalog.

**Architecture:** A pure validator recomputes every line and header amount from the invoice's own frozen inputs (price, quantity, tax rate, inclusive flag, override and rule evidence) under the contract the invoice declares, and compares the result exactly with what was submitted. Two contracts exist: `legacy` (unversioned payloads; an exact replica of the JavaScript `toFixed` calculator, including final-line residual allocation) and `v1` (scaled-integer money, round half away from zero, capped largest-remainder allocation). A shared JSON fixture pins v1 for both C# and TypeScript. A mismatch, or an unknown version, quarantines the invoice as a `NeedsReview` result backed by a durable `ReconciliationIssue` whose id is returned as the case id.

**Tech Stack:** ASP.NET Core 10, Wolverine, ErrorOr, EF Core/PostgreSQL, xUnit + NSubstitute + Testcontainers; Angular 21, Vitest, Bun.

**Spec:** Backlog item 18 "Offline invoice arithmetic validation" (pasted brief, 2026-11-03), [market-readiness review](../../reviews/2026-10-21-market-readiness.md) R11, [business evidence](../../reviews/2026-10-21-market-readiness-evidence/backend-business.md) F5.

## Global Constraints

- Angular web + .NET API only; mobile excluded.
- Domain ← Application ← Infrastructure ← Api layering; repositories + `IUnitOfWork` for writes; no exception-driven business control flow.
- Bun only for the frontend; C# warnings are errors; no migration in this change (schema unchanged).
- v1 scales: money two decimals (paise), quantity three decimals, percentages two decimals (basis points) — these equal the existing column precisions (18,2 / 18,3 / 5,2).
- Never rewrite a submitted payload to make it balance; never compare with a tolerance.
- Legacy payloads must keep their idempotency hash byte-for-byte.

## Decisions

1. **Contract per invoice.** `calculationVersion` absent/null → `legacy`; `1` → `v1`; anything else → quarantine with `Sale.OfflineCalculationVersionUnsupported`.
2. **Legacy is an exact replica, not a tolerance.** The legacy calculator ran on IEEE doubles and rounded with `Number(x.toFixed(2))`, which is round-half-away-from-zero on the *exact binary* value. C# doubles use the same IEEE operations, so the validator replays the legacy chain step for step in `double` and rounds with an exact `BigInteger` decomposition of each double. Inputs are restored to the double the browser held via `double.Parse(decimal.ToString(InvariantCulture))` (JSON numbers round-trip).
3. **v1 is scaled-integer math in both languages** (`BigInteger` / `bigint`): `gross = roundDiv(priceCents × qtyMilli, 1000)`, inclusive `preTax = roundDiv(gross × 10000, 10000 + rateBp)`, `percent(base, bp) = roundDiv(base × bp, 10000)`, `tax = roundDiv(taxable × rateBp, 10000)`. Discount selection is unchanged from the legacy calculator (rule ⇒ configured amount; override ⇒ `min(configured, override)`; clamp to `[0, basis]`; services take no item or sale discount).
4. **v1 sale-discount allocation:** floor of `total × weight / eligible` per goods line with positive taxable value, capped at that line's taxable value; remaining paise go one each to the largest remainders, ties by client line order.
5. **v1 `totalBeforeDiscount` uses the domain definition** `Σ(lineTotal + itemDiscount + saleDiscount)` (what `Sale.Create` and online sales store); v1 adds `totals.subtotalBeforeDiscount = Σ preTax`. Legacy payloads may carry either `Σ preTax` (what the legacy browser sent) or the domain definition.
6. **Header identities (both contracts):** `subtotalBeforeDiscount = Σ preTax`, `totalDiscountAmount = Σ(item + sale discount)`, `totalTaxAmount = Σ tax`, `totalAmount = Σ lineTotal`; the existing `paid + due = total` check stays. A sale threshold rule must have its threshold met by the eligible subtotal.
7. **Input checks:** enum values must be defined; tax rate and percentages in `[0, 100]`; prices, flat overrides and thresholds non-negative and bounded (quantity ≤ 10,000,000, money inputs ≤ ₹100,000,000,000 so every intermediate stays exact); v1 additionally rejects inputs beyond its scales (`scale_exceeded`); a rule id without its percentage (or a threshold rule without its threshold) is `rule_evidence_missing` in both contracts — every browser build sent both.
8. **Quarantine path:** one `ReconciliationIssue` of new type `ArithmeticConflict = 7` (enum stored as int, no migration) with the error code and a ≤1000-char evidence summary; `NeedsReview` result carries `reviewCaseId` and up to 100 structured mismatches `{path, supplied, expected, code}`. Every review result now returns its case id. The device keeps the original frozen payload (NeedsReview records are never deleted or retried); the immutable server-side envelope and resolution console remain backlog 19.
9. **Validation runs after structural line checks and lease/invoice checks, before `ISaleLineValidator`, entity construction, stock consumption and ledger posting.** Catalog variance (price, rule) stays a separate warning path.
10. **Posted sales:** a read-only SQL audit lists posted offline sales whose header and lines disagree; it never edits stock or ledgers.

Mismatch codes: `amount_mismatch`, `discount_exceeds_basis`, `scale_exceeded`, `unsupported_value`, `rule_evidence_missing`, `rule_threshold_not_met`, `unsupported_version`. Paths: `totalAmount`, `items[2].taxAmount`, `calculationVersion`, …

## Review Focus

- RF1: Line ₹118 with header/paid ₹1 → NeedsReview, path `totalAmount` supplied 1 expected 118; no sale, stock transaction or customer ledger entry. *(Task 1 `Header_must_equal_lines`, Task 3 handler + integration)*
- RF2: 0.25 × 118 inclusive at 18% and 0.25 × 100 exclusive both give 25.00 / 4.50 / 29.50; zero-rate and service inputs pinned. *(fixture + Task 1, Task 4)*
- RF3: ₹0.01 over three equal lines → [0.01, 0, 0]; ₹0.06 over ten → six 0.01 then four 0; no allocation negative or above its basis; legacy residual allocation still accepted for legacy bills. *(fixture + Task 1, Task 4)*
- RF4: Frozen ₹118 survives a current catalog price of ₹200 with a pricing-variance warning, amounts unchanged. *(Task 3 handler + integration)*
- RF5: Unknown `calculationVersion` and an item discount above its basis → NeedsReview with field evidence and no business mutation; v1 and legacy hashes keep their identities. *(Task 1, Task 2, Task 3)*

---

### Task 1: Arithmetic validator and shared v1 fixture

**Files:**
- Create: `tests/contracts/offline-invoice-arithmetic-v1.json`
- Create: `src/backend/Intelibill.Application/Features/Sales/Commands/SyncOfflineSales/OfflineInvoiceArithmeticValidator.cs` (entry, input checks, comparison)
- Create: `.../SyncOfflineSales/OfflineInvoiceArithmeticV1.cs`, `.../SyncOfflineSales/OfflineInvoiceArithmeticLegacy.cs` (expected-amount calculators), `.../SyncOfflineSales/OfflineArithmeticMismatch.cs`
- Modify: `SyncOfflineSalesCommand.cs` (append `int? CalculationVersion = null` to `OfflineSaleSyncCommand`)
- Modify: `tests/backend/unit/Intelibill.Application.Unit.Tests/Intelibill.Application.Unit.Tests.csproj` (copy the fixture to output as `Contracts/offline-invoice-arithmetic-v1.json`)
- Test: `tests/backend/unit/.../SyncOfflineSales/OfflineInvoiceArithmeticValidatorTests.cs`

**Interfaces — Produces:**
- `public sealed record OfflineArithmeticMismatch(string Path, decimal? Supplied, decimal? Expected, string Code)`
- `internal static class OfflineInvoiceArithmeticValidator { const int CurrentVersion = 1; static OfflineArithmeticVerdict Validate(OfflineSaleSyncCommand sale); }`
- `internal sealed record OfflineArithmeticVerdict(string Contract, IReadOnlyList<OfflineArithmeticMismatch> Mismatches) { bool IsConsistent; bool IsUnsupportedVersion; }` — `Contract` is `"legacy"`, `"v1"` or `"unsupported"`.
- Fixture case shape: `{ name, input: { lines: [{ lineType, quantity, salesPrice, taxRatePercent, taxIncluded, itemDiscount: {type,value}, batchRulePercentage }], saleDiscount: {type,value}, saleRule: {ruleType, percentage, thresholdAmount} | null, paidAmount }, expected: { lines: [{ preTaxAmount, itemDiscountAmount, saleDiscountAmount, taxableAmount, taxAmount, lineTotal }], totals: { subtotalBeforeDiscount, totalBeforeDiscount, totalDiscount, totalTax, grandTotal, paidAmount, dueAmount } } }`. Expected values are generated by an independent exact-rational reference, then frozen.

- [ ] Write tests: every fixture case, posted as a v1 command, is consistent; `Header_must_equal_lines` (RF1: line 100/18/118, header total/paid 1 → mismatch `totalAmount` supplied 1 expected 118); `Inclusive_exclusive_fractional_parity` (RF2); `Allocation_remainder_is_deterministic` (RF3, plus a tampered allocation `[0, 0.01, 0]` rejected); `Legacy_residual_allocation_is_accepted` and `Legacy_tofixed_tie_is_replayed_exactly` (values captured from the current browser calculator before Task 4 replaces it); `Discount_exceeds_basis` (code `discount_exceeds_basis`); `Unknown_version_is_unsupported`; `V1_rejects_excess_scale`; `Undefined_enum_is_unsupported_value`; `Threshold_rule_not_met`.
- [ ] Run `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter FullyQualifiedName~OfflineInvoiceArithmeticValidatorTests` — red.
- [ ] Implement the validator and calculators per Decisions 2–7.
- [ ] Re-run — green. Commit.

### Task 2: Calculation version in the request and idempotency hash

**Files:** `SalesControllerRequests.cs` (`int? CalculationVersion = null` on `OfflineSaleSyncRequest`), `SalesController.Offline.cs` (map it), `OfflineSaleSyncIdempotencyHasher.cs` (append `[property: JsonIgnore(Condition = WhenWritingNull)] int? CalculationVersion`).
**Test:** `tests/backend/unit/.../SyncOfflineSales/OfflineSaleSyncIdempotencyHasherTests.cs`

- [ ] Write `Legacy_hash_is_unchanged` (literal hash captured from the current hasher for a fixed command) and `Version_changes_hash`. Red → implement → green. Commit.

### Task 3: Enforce before posting and return review evidence

**Files:** `SyncOfflineSalesCommandHandler.cs`, `OfflineSaleSyncHelpers.cs`, `OfflineSalesSyncDtos.cs` (`Guid? ReviewCaseId`, `IReadOnlyList<OfflineArithmeticMismatch> Mismatches` init properties), `IOfflineSaleVarianceAnalyzer.cs` / `OfflineSaleVarianceAnalyzer.cs` (`AddReviewIssueAsync` returns `Task<Guid>`), `Domain/Enums/ReconciliationIssueType.cs` (`ArithmeticConflict = 7`), `Errors.Sale.cs` (`OfflineArithmeticMismatch`, `OfflineCalculationVersionUnsupported`).
**Tests:** `SyncOfflineSalesCommandHandlerTests.cs`, `tests/backend/integration/.../SalesControllerTests.cs`

- [ ] Handler tests: `Arithmetic_failure_has_no_posting` (RF1: NeedsReview, `ArithmeticConflict` issue added, case id equals issue id, no `ValidateLinesAsync`, sale, stock or ledger calls); `Unknown_version_quarantined`; `Discount_exceeds_basis_quarantined`; `Catalog_change_preserves_frozen_amounts` (RF4: batch now 200, frozen 118 persisted, pricing variance warning). Correct existing tests whose fixtures had inconsistent arithmetic.
- [ ] Integration tests through `POST api/sales/offline-sync`: RF1 (stock, sale count and receivable unchanged, reconciliation issue present), RF4 (v1 payload, catalog changed after issuance), unknown version.
- [ ] Implement per Decisions 8–9. Run `--filter FullyQualifiedName~SyncOfflineSales` and integration `--filter FullyQualifiedName~SalesControllerTests` — green. Commit.

### Task 4: Browser v1 math and fixture parity

**Files:** Create `src/frontend/src/app/features/sales/services/offline-money.math.ts` + `.spec.ts`; modify `offline-sale-pricing-calculator.ts` (+ spec), `offline-sale-core.types.ts`.

**Interfaces — Produces:** `OFFLINE_CALCULATION_VERSION = 1`; `toScaled(value: number, scale: number): bigint` (decimal-string parse, half away from zero); `fromScaled(value: bigint, scale: number): number`; `roundDiv(numerator: bigint, denominator: bigint): bigint`; `allocateLargestRemainder(total: bigint, weights: readonly bigint[]): bigint[]` (each weight is also its cap). `OfflineFrozenSalePricing.calculationVersion?: number`; `OfflineFrozenSaleTotals.subtotalBeforeDiscount?: number`.

- [ ] Specs: math helpers (half-away midpoints, negative, exponent strings, allocation ties); calculator consumes every fixture case and matches exactly; repeated edits of the same cart give identical numbers; output carries `calculationVersion: 1`. Red → implement → green. Commit.

### Task 5: Versioned queue payloads and NeedsReview reasons

**Files:** `utils/offline-sale-sync.mapper.ts` (+ spec), `services/sale.models.ts`, `services/offline-sales-queue-indexeddb.service.ts`, `services/offline-sales-queue-sync.service.ts`, `services/offline-sale-finalization.service.spec.ts`, `pages/sales-page.component.{ts,html,scss}` (+ spec), `public/assets/i18n/*.json`.

- [ ] Mapper sends `calculationVersion` only when present and `subtotalBeforeDiscount` from the new total (legacy records map exactly as before, so their hash is unchanged); result mapping keeps `reviewCaseId` and `mismatches`; finalization queues a v1 payload before returning it for print.
- [ ] Sales page lists NeedsReview invoices (invoice number, translated reason, mismatch paths, case id) with no retry or recalculate action.
- [ ] `bun run test --run src/app/features/sales` and `bun run build` from `src/frontend` — green. Commit.

### Task 6: Posted-sale audit and documentation

**Files:** Create `scripts/db/audit-offline-sale-arithmetic.sql` (read-only report); modify `docs/backend-architecture.md` (§ Offline Invoice Arithmetic), `CLAUDE.md` (one infrastructure line). Test: `SalesControllerTests.OfflineSaleArithmeticAudit_ReportsPostedSalesWhoseHeaderDisagreesWithLines` runs the script as the tenant-bound runtime role.

- [ ] Script reports posted `source = Offline` sales where header ≠ Σ lines or a line's taxable + tax ≠ total; no writes. Commit.

## Rollout and rollback

The server accepts legacy and v1 together, so it deploys first; browsers start writing v1 on their next load. Already-queued legacy payloads keep syncing under the legacy replica. Rolling back the frontend alone is safe (legacy payloads remain valid); rolling back the backend requires the frontend rollback first, because the old server ignores `calculationVersion` but would still trust totals. Mismatched invoices stay on the device in NeedsReview and as reconciliation issues on the server; nothing is auto-corrected.

## Deferred (owned elsewhere)

- Immutable server-side received envelope, case resolution API and console: backlog 19.
- Tax applicability and invoice snapshot fields: backlog 20. Inclusive-price economics (e.g. ₹100 inclusive at 18% totalling ₹100.01): backlog 28 starts v2 if it changes them.
- The offline sync API does not yet map `lineType`/`serviceId`; services stay blocked offline in the UI.

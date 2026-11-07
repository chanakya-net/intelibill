# Supplier Returns and Receipt Corrections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship source-linked supplier shipments, accepted credits, eligible receipt reversals and recoverable API/web workflows that reconcile stock, payable and PO history.

**Architecture:** Immutable shipment/credit/reversal facts reference existing purchase receipt values and original batches. Financial operation receipts own atomic writes; supplier/receipt row locks plus existing stock row versions coordinate with payments, cost correction, sales, counts and intake. A common web feature serves direct inbound, PO receipt history and supplier history using durable scope-owned requests.

**Tech Stack:** .NET 10/C#/EF Core/PostgreSQL, ErrorOr/FluentValidation, xUnit/Testcontainers; Angular 21/PrimeNG/Transloco, Bun, Vitest and Playwright.

**Spec:** `docs/superpowers/specs/2026-11-07-supplier-returns-receipt-corrections-design.md`

## Global Constraints

- Web and API only; no mobile changes.
- Preserve Domain ← Application ← Infrastructure ← Api, ErrorOr, FluentValidation, repository abstractions and IUnitOfWork.
- Use decimal money with two decimal places and QuantityPolicy with the item's quantity scale (0–3).
- Require authenticated active shop membership; Owner/Manager may ship returns and read operational history; only Owner may accept supplier credit or reverse a receipt. A Manager may create a pending shipment only.
- Every new mutation requires operationKey (1–128 characters), reason (3–255 characters), expected versions, and a single atomic transaction.
- Business dates use IBusinessClock and the shop's existing business time zone; no client backdating in this feature.
- New tenant tables use explicit shop predicates and ENABLE/FORCE RLS with USING and WITH CHECK.
- No legal GST credit/debit document claim, independent tax calculation, cash refund workflow, shipping-cost subsystem, automatic procurement reopening, historical inference, or replacement-batch editing.
- Continue in the existing `codex/supplier-returns-receipt-corrections` checkout; no worktree or approval pause. User-selected execution is Sol, followed by fresh thorough review/fixes and a PR.

## Review Focus

1. Paid receipt returned after allocation: credit must stay visible without altering old allocations, overstating payable or inventing cash; Task 3 pins this.
2. Sale/count/intake/payment races after preview: at most available goods and accepted credit commit, with both stock rows and all facts atomic; Tasks 2–4 pin this.
3. Timeout followed by 404, reload, tab conflict or shop switch: preserve exact request identity and prevent duplicates/wrong-scope sends; Task 6 pins this.
4. Restored stock, legacy evidence or one bad lot in a PO receipt: refuse unsafe full reversal without partial changes; Task 4 pins this.
5. Pending credits and printed history after supplier/item edits: source facts and document classification remain truthful; Tasks 5 and 7 pin this.

---

## File and interface map

Paths below are repository-relative. Follow existing adjacent command/query/validator/repository layouts. New types and members in this plan are proposed contracts, not claims that they already exist. No new packages are required.

- Domain facts: `src/backend/Intelibill.Domain/Entities/{SupplierReturn,SupplierReturnLine,SupplierReturnCredit,SupplierReturnCreditLine,PurchaseReceiptReversal}.cs`; enums under the existing `Enums/` folder.
- Persistence: matching configurations under `src/backend/Intelibill.Infrastructure/Data/Configurations/`, repositories under `Repositories/`, `Data/ApplicationDbContext.cs`, `DependencyInjection.cs`, generated additive migration and snapshot under `Migrations/`.
- Commands/queries/DTOs/policy: `src/backend/Intelibill.Application/Features/SupplierReturns/`; receipt reversal and receipt detail under existing `Features/PurchaseReceipts/`. Keep shipment, acceptance, reversal, eligibility and query projection separate.
- Existing seams: `Application/Common/Operations/IFinancialOperationExecutor.cs`, `Common/Posting/IPostingPeriodPolicy.cs`, `Common/Time/IBusinessClock.cs`, `Domain/Interfaces/Repositories/IPaymentReceiptRepository.cs`, and batch/inventory/receipt-value repositories.
- New API controllers: `src/backend/Intelibill.Api/Controllers/SupplierReturnsController.cs` and `PurchaseReceiptsController.cs`; add grouped reversal in the latter using its explicitly named route.
- New web feature: `src/frontend/src/app/features/supplier-returns/{services,components,pages}/`; integrate existing inventory, purchase-orders and suppliers surfaces without duplicating the editor.

Shared DTO contract lives in `Application/Features/SupplierReturns/DTOs/SupplierReturnDtos.cs`: `SupplierReturnLineInput(Guid ReceiptValueId, uint ExpectedReceiptVersion, uint ExpectedBatchVersion, decimal Quantity, decimal ProposedCreditAmount)`; `SupplierReturnCreditLineInput(Guid ReturnLineId, decimal AcceptedAmount)`; `SupplierReturnPreviewDto`, `SupplierReturnDetailDto` and `SupplierReturnSummaryDto` carry the spec's named fields. Receipt DTOs in `Features/PurchaseReceipts/DTOs/PurchaseReceiptDetailDto.cs` include history/eligibility/version/PO lineage. Reversal results in `PurchaseReceiptReversalResultDto.cs` contain the complete list of reversed lots, their source IDs, quantities, exact amounts and movement/ledger IDs. Never overload an unrelated invoice or payment DTO to carry these facts.

## Task 1: Persist auditable supplier return and reversal facts

**Files:** Create the five Domain entities and `Enums/SupplierReturnStatus.cs`; modify `Enums/{FinancialOperationType,SupplierLedgerEntryType,StockTransactionType}.cs` and `Entities/SupplierLedgerEntry.cs`. Create corresponding EF configuration files, `Domain/Interfaces/Repositories/ISupplierReturnRepository.cs`, `IPurchaseReceiptReversalRepository.cs` and their Infrastructure implementations; modify DbContext/DI; generate `AddSupplierReturnsAndReceiptReversals` migration. Tests: new `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/SupplierReturnTests.cs`, integration `SupplierReturnWorkflowTests.cs`, extend `RestrictedRoleRlsTests.cs`/`UpgradedSchemaRlsTests.cs`.

**Interfaces:** `ISupplierReturnRepository` provides scoped `GetDetailAsync(Guid shopId, Guid returnId, CancellationToken)`, `GetByReceiptsAsync(Guid shopId, IReadOnlyCollection<Guid> receiptValueIds, CancellationToken)`, `AddAsync(SupplierReturn, CancellationToken)`, `AddCreditAsync(SupplierReturnCredit, CancellationToken)` and `Update(SupplierReturn)`. `IPurchaseReceiptReversalRepository` provides scoped `GetByReceiptsAsync(...)` and `AddAsync(PurchaseReceiptReversal, CancellationToken)`. Each collection method returns `Task<IReadOnlyList<TEntity>>`; detail returns nullable entity. Entities expose immutable facts listed in the spec and guarded creation/one-time acceptance using ErrorOr; no public arbitrary setters.

- [ ] Write failing `PendingShipment_HasNoCredit`, `AcceptCredit_OnceOnly`, `ZeroCredit_HasEvidenceWithoutLedger`, `InvalidQuantityMoneyReason_Refused` unit tests; pin max money `99_999_999.99m`, positive quantities, 2-decimal credit, `PendingSupplierCredit → CreditAccepted` only, unique receipt lines and immutable source snapshots.
- [ ] Run `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter FullyQualifiedName~SupplierReturnTests`; confirm failures name missing new behavior.
- [ ] Implement entities and append enums exactly: operations 14/15/16, ledger credit 6, movement supplier return 9. Enforce negative amounts and batch-required for the new ledger kind; allow zero acceptance only without a ledger entry.
- [ ] Add persistence, enum conversions, unique credit-per-return, credit-line-per-return-line, reversal-per-receipt and return-number-per-shop constraints; amount/quantity checks, tenant FKs and RLS USING/WITH CHECK. Generate migration using repository EF commands; no inferred historical return backfill.
- [ ] Add fresh/upgrade migration and restricted-role tests: owner/manager current-shop facts readable; other-shop SELECT empty and forged child INSERT/UPDATE rejected; pre-existing receipt/payment facts unchanged.
- [ ] Run Domain tests and focused RLS/migration integration tests; inspect generated migration for additive-only rollout and correct constraints, then commit `feat: persist supplier return audit facts`.

## Task 2: Preview and atomically ship available goods

**Files:** Create `Application/Features/SupplierReturns/Services/{ISupplierReturnMutationLock,SupplierReturnPolicy}.cs`, `Infrastructure/Services/SupplierReturns/SupplierReturnMutationLock.cs`, command files under `Features/SupplierReturns/Commands/RecordSupplierReturn/`, preview query/handler under `Queries/PreviewSupplierReturn/`, DTOs and `Common/Errors/Errors.SupplierReturn.cs`. Create `Api/Controllers/SupplierReturnsController.cs`. Modify operation enum slug mapping in `Features/FinancialOperations/FinancialOperationTypes.cs`, DI and `Infrastructure/Data/Configurations/{StockTransactionConfiguration,SupplierLedgerEntryConfiguration}.cs` plus affected stock/report readers. Persist the new strings as `SUPPLIER_RETURN` and `SUPPLIER_RETURN_CREDIT`. Tests: new `SupplierReturnPolicyTests.cs` under Application unit `Features/SupplierReturns/`, integration `SupplierReturnWorkflowTests.cs`.

**Interfaces:** `ISupplierReturnMutationLock.LockAsync(Guid shopId, Guid supplierId, IReadOnlyCollection<Guid> receiptValueIds, CancellationToken) -> Task<RowLockOutcome>` requires an existing transaction and takes supplier then sorted receipt locks, with 5 s timeout. `RecordSupplierReturnCommand(Guid ActorUserId, Guid ActiveShopId, string OperationKey, Guid SupplierId, string Reason, bool CreditAccepted, string? SupplierReference, IReadOnlyList<SupplierReturnLineInput> Lines)` → `HandleAsync(..., CancellationToken): Task<ErrorOr<SupplierReturnDetailDto>>`. `PreviewSupplierReturnQuery` takes authenticated actor/shop, supplier and receipt/quantity pairs → preview DTO. `SupplierReturnPolicy.ReturnableQuantity(decimal available, decimal received, decimal shipped): decimal` is `max(0,min(available,received-shipped))`.

- [ ] Write failing `PartlySoldReceipt_ReturnsOnlyAvailableUnits`: receive 10/₹1000, sell 4, pending shipment 3 leaves batch/aggregate 3, exactly one -3 supplier-return movement, supplier payable ₹1000, received 10 and net retained 7. Also pin over-return, duplicate lines, fractional scale violations, cross-shop/wrong-supplier, missing provenance and count-added quantity ceiling; no side effects on failure.
- [ ] Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~SupplierReturnWorkflowTests` and the focused policy unit tests; establish red.
- [ ] Implement advisory preview and record command/controller with validation, active role checks, canonical hash and operation scope 14. Fence date, acquire supplier/receipt locks without changing xmin, then freshly load/version-check rows. Resolve same-supplier direct/PO lineage server-side. Apply one signed movement and batch/aggregate decrement per line; explicitly update receipt rows to advance xmin. Save through executor, no nested transactions or precommit success notification.
- [ ] Add `DuplicateShipment_OneEffect`, `ChangedPayload_KeyConflict`, `RoleRevokedBeforeReplay_Denied`, `WrongSupplierOrTenant_RollsBackAllFacts`, `MultiLineFailure_RollsBackAll`, `PendingShipment_ExcludedFromSalesAndStockLoss` and `ShipmentVersusSaleCountIntake_NoLostStock` integration tests using independent contexts and bounded synchronization, not sleeps. Assert original batch cost and issued-sale snapshots unchanged, aggregate equals movement sum, and race loser has no shipment/operation receipt.
- [ ] Run focused integration/policy tests and relevant inventory/sales tests. If a writer lacks the existing shared xmin protocol, repair that specific path and test it; avoid an unrelated global locking rewrite. Commit `feat: record source-linked supplier shipments`.

## Task 3: Accept credit once and reconcile balances

**Files:** Create command files under `Application/Features/SupplierReturns/Commands/AcceptSupplierReturnCredit/` and focused `Services/SupplierReturnCreditPoster.cs`; extend return repository and controller. Modify `Infrastructure/Services/Payments/InvoiceBalanceReader.cs`, `Application/Features/Payments/{DTOs/PaymentDtos.cs,Services/IInvoiceBalanceReader.cs}`, supplier ledger DTO/query projection, and `Features/PurchaseReceipts/Commands/CorrectReceiptCost/CorrectReceiptCostCommandHandler.cs`, `Features/Inventory/Commands/ReassignBatchSupplier/ReassignBatchSupplierCommandHandler.cs`. Tests: extend workflow tests and payment/correction handler tests.

**Interfaces:** `AcceptSupplierReturnCreditCommand(Guid ActorUserId, Guid ActiveShopId, Guid ReturnId, string OperationKey, uint ExpectedVersion, string Reason, string? SupplierReference, IReadOnlyList<SupplierReturnCreditLineInput> Lines)` → `Task<ErrorOr<SupplierReturnDetailDto>>`. `SupplierReturnCreditPoster.PostAsync(SupplierReturn returnDocument, IReadOnlyList<SupplierReturnCreditLineInput> lines, Guid actorId, string reason, string? supplierReference, CancellationToken)` returns `Task<ErrorOr<Success>>` inside caller's transaction; shared by immediate acceptance from Task 2 and later acceptance. Supplier balance DTO appends `ReturnCreditAmount`, `NetPayableAmount` and `SupplierCreditAmount` (decimal, compatible default 0); existing `PayableAmount` remains the effective receipt cost and `OutstandingAmount` becomes max(0, net payable minus active allocations). Original receipt totals and effective cost are unchanged by credits.

- [ ] Write failing `PendingThenAccepted_OneCreditOnAcceptanceDate`: 10/₹1000, ship 3 pending, approve ₹300 → payable ₹700, stock unchanged on approval, linked credit evidence/ledger. Pin immediate accepted shipment equivalent, manager acceptance forbidden, zero acceptance without ledger, duplicate replay, different key after acceptance conflict, and acceptance above remaining receipt credit ceiling refused.
- [ ] Write `PaidReceiptReturn_PreservesAllocationsAndShowsSupplierCredit`: allocated ₹1000, accepted ₹300 → net payable ₹700, outstanding ₹0, supplier credit ₹300, original allocations ₹1000, account balance -₹300. Verify no payment/expense/refund created, and new allocation cannot target already-settled net payable. A ₹500 allocation + ₹300 credit instead leaves ₹200 outstanding.
- [ ] Run focused workflow/payment tests to establish red.
- [ ] Implement Owner-only acceptance, operation scope 15, date fence → supplier/receipt locks → return lock/version check. Require every line exactly once, cap cumulative credit per receipt, create one negative linked ledger entry per positive amount and immutable credit facts. Wire immediate acceptance into the shipment's same atomic operation. Update balance projection and source-linked supplier ledger history.
- [ ] Guard cost correction and supplier reassignment after any shipment, including pending, with `PurchaseReceipt.HasSupplierReturns`; use receipt xmin writes to protect against existing commands that read before shipment commits. Pin `LegacyBatchUpdate_CannotReassignAfterShipment` against `Features/Inventory/Commands/UpdateInventoryBatch/UpdateInventoryBatchCommandHandler.cs` (currently rejects supplier changes with `Inventory.BatchSupplierEditMoved`), and confirm bulk intake creates new receipts rather than updating old source facts. Test these races, approval-versus-allocation serialization, competing approvals and closed current period with no side effects.
- [ ] Run workflow/payment/correction tests and commit `feat: reconcile accepted supplier credits`.

## Task 4: Reverse wholly erroneous receipts without bypasses

**Files:** Create `Features/PurchaseReceipts/Services/PurchaseReceiptReversalPolicy.cs`, command files under `Commands/ReversePurchaseReceipt/`, receipt detail DTOs/results, new `Api/Controllers/PurchaseReceiptsController.cs`. Modify `Features/Inventory/Commands/VoidBatch/VoidBatchCommandHandler.cs` to share eligibility guards and linked history or delegate to the shared posting service while preserving its public response. Add operation slug 16. Tests: new integration `PurchaseReceiptReversalTests.cs`; extend existing void handler tests.

**Interfaces:** `ReceiptReversalLineInput(Guid ReceiptValueId, uint ExpectedReceiptVersion, uint ExpectedBatchVersion)`. `ReversePurchaseReceiptCommand(Guid ActorUserId, Guid ActiveShopId, string OperationKey, string Reason, Guid TargetId, bool IsPurchaseOrderReceipt, IReadOnlyList<ReceiptReversalLineInput> Lines)` → `Task<ErrorOr<PurchaseReceiptReversalResultDto>>`; controller constructs one-line input for a lot and validates exact source lot-set for a PO group. `PurchaseReceiptReversalPolicy` produces explicit eligibility codes from resolved receipt, batch state, stock/count/adjustment history, return facts and active allocations; preview and posting use the same decisions.

- [ ] Write failing `UnsoldUnallocatedReceipt_ReversesExactEffectiveTotal`: receive 3/₹100, cost-correct to ₹110, reverse → one -3 movement, -₹110 ledger, voided original batch, zero aggregate, preserved source and correction facts. Pin zero-cost reversal and repeated-key replay.
- [ ] Add `ConsumedThenRestored_StillIneligible`, `CountHistory_StillIneligible`, `AllocatedOrReturnedOrLegacy_Refused`, `PreviouslyReversedPayment_NoActiveAllocation_AllowsReversalWithAuditPreserved`, `GroupOneBadLot_RollsBackEveryLot`, `GroupValid_ReversesAllLotsOnce`, `ClosedOriginalOrCurrentPeriod_Refused`, and `OldVoidRoute_CannotBypassSupplierReturnOrAllocationGuards`.
- [ ] Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~PurchaseReceiptReversalTests`; establish red.
- [ ] Implement Owner-only scope 16 reversal. Fence original and current dates before supplier/receipt locks, reload/version-check every source, revalidate eligibility, reverse exact effective value and entire receipt quantity, create immutable reversal records, mark original batches voided and update receipt/batch/aggregate versions atomically. Never modify PO received/remaining/status. Existing void route must not create a second reversal for a receipt reversed through the new route.
- [ ] Add reversal-versus-payment/sale/count/cost-correction tests and compare movement, ledger and operation rows after both race outcomes. Run focused tests plus existing void/PO suites; commit `feat: reverse eligible purchase receipts atomically`.

## Task 5: Expose receipt/return history and honest purchase quantities

**Files:** Create return list/detail and receipt-detail query handlers in new feature folders; extend `Domain/Interfaces/Repositories/IPurchaseReceiptValueRepository.cs` and Infrastructure repository with scoped receipt lookups/read projections as needed. Modify `Application/Features/PurchaseOrders/{DTOs/PurchaseOrderDtos.cs,Services/PurchaseOrderDtoMapper.cs}` and its detail query/repository path, `Features/Inventory/DTOs/InventoryBatchDto.cs` and batch query mapping; `Features/SupplierLedger/{DTOs/SupplierLedgerEntryDto.cs,Queries/GetSupplierEntries/GetSupplierEntriesQueryHandler.cs}`. Tests: extend workflow and `PurchaseOrdersControllerTests.cs`.

**Interfaces:** `GetPurchaseReceiptDetailQuery(Guid ActorUserId, Guid ActiveShopId, Guid ReceiptValueId)` → `Task<ErrorOr<PurchaseReceiptDetailDto>>`; `GetSupplierReturnQuery(..., Guid ReturnId)` → detail; list query accepts supplier/receipt/status and page/pageSize. Append nullable receipt-value ID, returned/reversed/net-retained quantities to PO receipt-line DTO, and corresponding totals to PO line/detail DTO. A focused read projection enriches the existing synchronous mapper; do not turn it into per-row async queries.

- [ ] Write failing `DirectAndPoReceipts_ShareHistory`, `PendingShipment_IsDiscoverableWithoutLedger`, `ReceivedQuantity_RemainsHistorical`, `SupplierLedgerCredit_LinksToReturn`, `ReadHistory_CrossShopHidden`, `PrintSnapshot_SurvivesSupplierAndItemRename`. Pin 10 received, 3 shipped → received10/net retained7; reversing another untouched lot decreases net retained only.
- [ ] Run focused workflow/PO tests; establish red.
- [ ] Implement the GET routes, bounded list queries and batched projections. Receipt detail returns versions, eligibility reasons and all shipment/acceptance/reversal links. PO history includes persisted receipt-value identity, not a guessed batch identifier. Do not change operation replay's historical response schema expectations; added DTO fields have compatible defaults.
- [ ] Run query/PO/operation tests, check no N+1 history queries, then commit `feat: expose supplier return and receipt history`.

## Task 6: Recoverable web API client and mutation drafts

**Files:** Create `src/frontend/src/app/features/supplier-returns/services/{supplier-return.models.ts,supplier-return.service.ts,supplier-return-draft.service.ts,supplier-return-errors.ts}` and companion specs. Modify `core/auth/auth.constants.ts` for endpoints and lookup slugs. Reference existing `features/purchase-orders/services/{receiving-draft.service.ts,receiving-errors.ts}` and `core/context/{shop-scope.ts,scoped-request.ts}`.

**Interfaces:** Client methods `preview`, `record`, `acceptCredit`, `reverseReceipt`, `reversePurchaseOrderReceipt`, `getReceipt`, `getReturn`, `listReturns`, `getOperation` mirror API DTOs and return Observables. Every mutation/recovery takes `ScopeOwner`. Draft identity is `[userId, shopId, actionKind, targetId]`; frozen record contains `operationKey`, exact payload, revision, sending/unknown status and operation-specific confirmation evidence. Service methods `load`, `save`, `prepare`, `claimRecovery`, `settleFailure`, `settle`, `isConfirmed`, `clearConfirmed`, `discard` follow receiving semantics with typed action union `shipment | credit | reversal`.

- [ ] Write failing specs for exact frozen replay after reload, 404 after uncertain send retaining payload, definitive initial validation preserving editable fields, later error retaining unknown status, cross-tab revision conflicts, stale response after shop switch, two tabs confirming then cleaning, and storage-unavailable preventing HTTP.
- [ ] Run `bun run test --run src/app/features/supplier-returns/services/supplier-return-draft.service.spec.ts` from `src/frontend`; establish red.
- [ ] Implement durable IndexedDB claim/confirmation boundary using a new feature database and captured owner. Prepare must commit before sending, uncertainty cannot generate a new key or editable payload, and only the matching confirmation can clear a draft. Use scoped request context for write and lookup. Do not weaken or refactor #799's receiving durability.
- [ ] Run new service specs and existing receiving-draft/service specs; commit `feat: recover supplier return submissions safely`.

## Task 7: Complete direct-inbound, PO and supplier web flows

**Files:** Create `features/supplier-returns/pages/{purchase-receipt-detail-page,supplier-return-detail-page}.component.{ts,html,scss}`, components `{supplier-return-dialog,supplier-return-credit-dialog,supplier-return-print-view}.component.{ts,html,scss}` and companion specs; add routes in `src/frontend/src/app/app.routes.ts`. Modify inventory batches list/detail surface, `features/purchase-orders/components/purchase-order-receipt-history.component.ts`, PO page/service models, `features/suppliers/components/supplier-detail.component.{ts,html}` and its ledger table/service models. Update all nine `src/frontend/public/assets/i18n/*-IN.json`; create feature `i18n-coverage.spec.ts`.

**Interfaces:** Shared receipt page takes receipt-value route parameter; return page/print take return ID. The editor can start from a receipt lot or same-supplier selected lots and submits Task 6's models. PO receipt history links each actual receipt value and provides Owner group-reversal action using all lot versions. Supplier history list provides pending/accepted filters and navigates to the same detail/approval surface.

- [ ] Write failing component tests for available quantity/precision, explicit pending versus accepted selection, Manager pending-only controls, Owner acceptance/reversal, preview refresh after conflict, validation fields retained, disabled edits after uncertainty, and paid-return supplier credit labels.
- [ ] Run each new companion spec with `bun run test --run <path>`; establish red.
- [ ] Implement receipt details, multi-lot editor, pending credit acceptance, reversal confirmation with reason and eligibility explanations, history links and status. Display received and net retained separately in PO history without changing procurement remaining/status. Successful outcomes refresh stock, supplier balance and linked PO read state. Unknown outcomes expose lookup/retry of the exact operation.
- [ ] Implement print from frozen returned detail snapshots with “Supplier return reference” and “Commercial reference — not a GST credit/debit note”; show shipment and accepted dates/amounts distinctly. Add complete locale keys, semantic form controls, accessible progress/errors, responsive tables and keyboard focus handling.
- [ ] Add tests for pending history without a ledger entry, zero-credit accepted display, snapshot print after source edits, direct inbound navigation, PO grouped reversal and source links, supplier credit/ledger display and i18n key parity. Run all affected frontend specs plus build; commit `feat: complete supplier return web workflows`.

## Task 8: Release evidence, documentation and review fixes

**Files:** New `src/frontend/tests/ui-audit/release/supplier-returns.spec.ts` and focused fixture server if required; extend release configuration. Update `docs/backend-architecture.md`, `docs/frontend-architecture.md`, `AGENTS.md` and feature-facing docs with the final contracts/limits; retain the spec/plan as implementation record.

- [ ] Add production-build Playwright journeys for direct inbound → partial sale → pending shipment → reload recovery → acceptance → supplier ledger → print; PO multi-lot history/group reversal, role restrictions, narrow viewport and keyboard completion. Test an uncertain first request, operation lookup404 and cross-tab confirmation against the production bundle; use deterministic fixtures and also verify the real API flows through integration tests.
- [ ] Run `dotnet build src/backend/Intelibill.slnx` and `dotnet test src/backend/Intelibill.slnx` with Docker. Require all tests green; do not count an unavailable integration fixture as passing.
- [ ] From `src/frontend`, run `bun run test --watch=false`, `bun run build` and `bun run audit:ui:release` with supported Node >=24 <26. Preserve the protected service worker build/audit. Record existing versus new warnings accurately.
- [ ] Review additive migration deployment, restricted-role evidence, shared balance math, stock-type classification, version/lock order, immutable print facts, grouped reversal atomicity and operation recovery. Update architecture/AGENTS documentation with the actual routes, migration name, owner policies, paid-return credit semantics and no automatic refund/cancellation/reopening limits.
- [ ] Commit verified implementation/documentation. Hand the whole branch to a fresh Astra reviewer as requested, fix every actionable finding, and re-run affected checks. Parent opens a PR only after review fixes and final evidence; PR states migration-before-app deployment, exact validation and bounded limitations.

## Self-review record

Attachment coverage is complete: source-linked available returns and pending/accepted credit (Tasks 1–3), eligible receipt and grouped PO reversal (4), historical received versus net retained and all source histories (5), durable uncertain recovery (6), direct inbound/PO/supplier entry, acceptance and printable reference with i18n (7), tenancy/migration/upgrade/concurrency/release proof and final review (1–8). The old attachment's two large tasks are split by independently reviewable behavior. Policy decisions are documented in the spec, including paid supplier credit, no tax-document claim, current optimistic stock concurrency, and refusal to reprice/reassign receipts after shipment. No product implementation or passing-test claim is made by this planning document.

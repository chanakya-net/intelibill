# Expiry-aware Selling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply a versioned expiry policy consistently to online/offline selling and preserve returned unusable goods as audited physical quarantine.

**Architecture:** A pure domain evaluator consumes retained policy/date facts. Application services enforce live authority and a PostgreSQL policy fence, while issued offline evidence and disposition journals preserve history independently of mutable inventory and invoice documents.

**Tech Stack:** Existing .NET 10/C#/EF Core/PostgreSQL/ErrorOr/xUnit/Testcontainers and Angular 21/NgRx/PrimeNG/Bun; no new package required.

**Spec:** `docs/superpowers/specs/2026-11-08-expiry-aware-selling-design.md` (read completely before implementation).

## Global Constraints

- API and web only; mobile and regulated-stock compliance are excluded. No production merchant policy is enabled.
- Preserve Domain ← Application ← Infrastructure ← Api, ErrorOr, repositories, IUnitOfWork, warnings-as-errors and central package versions.
- Default `Ignore`, `ExpiryRequired=false`, `SellThroughExpiryDate=true`, `WarningDays=7`, `OverrideAllowed=false`; item override absent means inherit.
- Tenant/actor/live role come from authenticated context. Owner changes policy/disposition; Owner/Manager approves an enabled Warn exception. Hard blocks have no override.
- Use shop business dates; preserve invoice-document-v2, arithmetic-v1 and historical idempotency hash shapes when optional new fields are absent.
- Physical quantity includes quarantine. Never consume quarantine for sales, supplier shipments or generic adjustments; never silently clear it by count/date/policy edits.
- Lock order: operation receipt/reconciliation case -> posting period -> expiry policy fence -> till -> party -> document/credit -> batch/item. Never acquire expiry after batch.
- Work on the already-created `codex/expiry-aware-selling` branch in place. User authorized automatic Sol implementation after Astra planning, then thorough review/fixes and PR; no plan-approval pause or new worktree.

## Review Focus

1. Policy/date edit races a final post: one committed decision, no stale-policy success after the edit wins (Task 2 barrier test).
2. Partial/new offline fields or a changed-back Ignore policy masquerade as legacy authority: hold, never fallback (Task 3).
3. Duplicate sale lines or stale warning approval authorize the wrong quantity: summed exact binding and no mutation on refusal (Task 2).
4. Return void/disposal/adjustment/supplier mutation consumes another source's quarantine: preserve root identity and physical/subset conservation (Task 4).
5. Midnight/shop switch/reload occurs after preview or unknown HTTP outcome: reevaluate unsubmitted carts, preserve submitted payload/key and original evidence (Task 5).

## File and interface conventions

Below, backend paths start at `src/backend/`, frontend paths at `src/frontend/src/app/`, and test paths are repository-relative. Each named file is exact; a listed folder describes its existing files only where their shared change is mechanical (DI, DTO serialization, or migration output). Keep new services focused instead of expanding sale handlers with large policy implementations.

Common new domain contracts live in `Intelibill.Domain/ValueObjects/ExpiryPolicy.cs`:

```csharp
public enum ExpiryMode { Ignore = 0, Warn = 1, Block = 2 }
public sealed record ExpiryPolicy(long Version, ExpiryMode Mode, bool ExpiryRequired,
    bool SellThroughExpiryDate, int WarningDays, bool OverrideAllowed);
public sealed record ExpiryBatchFacts(Guid BatchId, Guid ItemId, DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate, decimal PhysicalQuantity, decimal QuarantinedQuantity,
    bool IsVoided, bool ItemActive, uint BatchVersion);
public sealed record SellabilityDecision(bool Allowed, string? ReasonCode, bool Warning,
    bool RequiresApproval, long PolicyVersion, long CatalogVersion, DateOnly BusinessDate,
    decimal PhysicalQuantity, decimal QuarantinedQuantity, decimal AvailableQuantity);
public static SellabilityDecision ExpiryPolicyEvaluator.Evaluate(
    ExpiryBatchFacts batch, ExpiryPolicy policy, long catalogVersion, DateOnly businessDate);
```

`Allowed` expresses lot eligibility before required warning approval; `RequiresApproval=true` must be satisfied at posting. Near-expiry warnings are informational. Hard-block/zero-stock decisions have AvailableQuantity=0. Otherwise available is physical minus quarantine. Existing specific item/stock errors remain authoritative where appropriate.

---

### Task 1: Persist and administer versioned policy

**Files:**
- Create: `Intelibill.Domain/ValueObjects/ExpiryPolicy.cs`, `Intelibill.Domain/Entities/ShopExpiryState.cs`, `Intelibill.Domain/Entities/ExpiryPolicyRevision.cs`, `Intelibill.Domain/Interfaces/Repositories/IExpiryPolicyRepository.cs`.
- Create: `Intelibill.Application/Features/Expiry/ExpiryPolicyService.cs`, `ExpiryPolicyDtos.cs`, `IExpiryPolicyFence.cs` in that same feature directory; `Intelibill.Application/Common/Errors/Errors.Expiry.cs`.
- Create: `Intelibill.Infrastructure/Repositories/ExpiryPolicyRepository.cs`, `Intelibill.Infrastructure/Services/Expiry/ExpiryPolicyFence.cs`, and matching entity configurations in `Intelibill.Infrastructure/Data/Configurations/`.
- Create: `Intelibill.Api/Controllers/ExpiryPoliciesController.cs`.
- Modify: layer `DependencyInjection.cs`, `Intelibill.Infrastructure/Data/ApplicationDbContext.cs`, `Intelibill.Application/Features/Shops/Commands/CreateShop/CreateShopCommandHandler.cs`; `Intelibill.Infrastructure/Migrations/<generated>_AddExpiryPolicies.cs` and generated designer/snapshot; `Intelibill.Infrastructure/Data/TenantPolicyManifest.cs`.
- Test: `tests/backend/unit/Intelibill.Domain.Unit.Tests/ValueObjects/ExpiryPolicyTests.cs`, `tests/backend/integration/Intelibill.Integration.Tests/ExpiryPolicyPersistenceTests.cs`, `ExpiryPolicyTests.cs` in the same integration directory.

**Interfaces:**
- `IExpiryPolicyFence.AcquireAsync(Guid shopId, bool exclusive, CancellationToken ct) -> Task<ErrorOr<Success>>`; requires caller transaction, 5-second bounded row lock, restores prior timeout.
- `IExpiryPolicyRepository.GetEffectiveAsync(Guid shopId, Guid itemId, DateTimeOffset? asOf, CancellationToken ct) -> Task<EffectiveExpiryPolicy>`; output includes Policy and CatalogVersion, with explicit Unknown history rather than guessed facts.
- `ExpiryPolicyService.ReadAsync(Guid shopId, Guid actor, Guid? itemId, CancellationToken ct)` and `UpdateAsync(Guid shopId, Guid actor, Guid? itemId, ExpiryPolicyUpdateRequest request, CancellationToken ct)` return `ErrorOr<ExpiryPolicyStateDto>` in Tasks.
- Request is `{expectedCatalogVersion:long, policy:ExpiryPolicySettings?, reason:string}`; null only valid for item inheritance. Settings omit server-generated Version. Output contains default, optional override, effective policy and catalog version. `ReadinessAsync` returns per-lot missing/expired/quarantined status and totals, using physical quantities.

- [ ] **Write failing evaluator and persistence tests.** Pin Ignore compatibility, missing-required hard block, each Warn/Block branch, warning window 0/7/365, inclusive/exclusive expiry, inactive/void, partial quarantine, invalid settings and services excluded by caller. Core boundary: `Evaluate(facts(expiry:2026-10-21), BlockInclusive,1,2026-10-21).Allowed == true`; same on 2026-10-22 is false with `Expiry.Expired`. Assert 10 physical/3 quarantine yields 7 available. Persist shop default, item override, reset-to-inherit and monotonically advancing versions; stale expected version and Staff/Manager writes fail without a revision.
- [ ] **Run red.** `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter FullyQualifiedName~ExpiryPolicyTests` and `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~ExpiryPolicyPersistenceTests`. New contracts/tests must fail before implementation.
- [ ] **Implement domain policy, history, fence and owner API.** Policy revisions retain full settings, target item/null, server timestamp, actor, reason and effective version. A reset appends an inheritance event. Seed compatible state and explicit immutable baseline only; new-shop state/initial revision are inserted in CreateShopCommandHandler under its existing tenant write scope and transaction. Missing state fails closed; history distinguishes the known pre-feature Ignore baseline from missing/corrupt revisions. Implement `GET/PUT /api/expiry-policies`, `GET/PUT /api/expiry-policies/items/{id}`, `GET /api/expiry-policies/readiness`. Use active membership and exact shop predicates. Add bounded readiness paging and totals; do not return all stock in an unbounded admin response.
- [ ] **Generate/inspect migration and verify green.** Check RLS under the fixture's restricted role, cross-shop item references, immutable revision rows, two concurrent updates (one succeeds, one version conflict), fresh-shop defaults and no quantity/date backfill. Run the above tests plus `dotnet build src/backend/Intelibill.slnx`.
- [ ] **Commit scoped files.** `feat: add versioned expiry policy`.

### Task 2: Enforce online sellability, warning audit and receipt dates

**Files:**
- Create: `Intelibill.Application/Features/Expiry/SaleExpiryGuard.cs`, `SaleExpiryContracts.cs`, `BatchExpiryRevisionService.cs`; `Intelibill.Domain/Entities/BatchExpiryRevision.cs`; `Intelibill.Domain/ValueObjects/SaleExpiryDecision.cs`; `Intelibill.Domain/Entities/Sale.Expiry.cs`.
- Modify: `Intelibill.Infrastructure/Repositories/InventoryBatchRepository.cs`; `Intelibill.Domain/Interfaces/Repositories/IInventoryBatchRepository.cs`; `Intelibill.Domain/Entities/InventoryBatch.cs`; `Intelibill.Application/Features/Inventory/Queries/GetAvailableBatches/GetAvailableBatchesQuery.cs`, `GetAvailableBatchesQueryHandler.cs`; inventory DTOs `AvailableBatchDto.cs`, `InventoryBatchDto.cs`; inventory controller mapping.
- Modify: `Intelibill.Application/Features/Sales/Services/SaleLineValidator.cs`, `ISaleLineValidator.cs`; `Commands/RecordSale/RecordSaleCommand.cs`, `RecordSaleCommandHandler.cs`, `RecordSaleIdempotencyHasher.cs`, `SaleDtoBuilder.cs`; `Queries/PreviewSale/PreviewSaleQueryHandler.cs`; `DTOs/SalePreviewDto.cs`, `SaleDto.cs` under that Sales feature; API sale request mapping.
- Modify: `Intelibill.Application/Features/Inventory/Services/InboundInventoryLineProcessor.cs`, `BatchRetailTermsReviser.cs`; `Commands/AddInventoryBatch/AddInventoryBatchCommandHandler.cs`, `Commands/UpdateInventoryBatch/UpdateInventoryBatchCommandHandler.cs`, `Commands/UpdateInventoryBatch/UpdateInventoryBatchCommand.cs`, `Commands/UpdateBatchPrices/UpdateBatchPricesCommandHandler.cs` under Inventory; `Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommandHandler.cs`; `Intelibill.Api/Controllers/InventoryController.Operations.cs`, `InventoryControllerRequests.cs`.
- Create/modify: matching batch-history repository/configuration, Sale/InventoryBatch configurations and migration `AddExpirySaleEvidence` (also adds zero-default quarantine column for Task 4).
- Test: `tests/backend/integration/Intelibill.Integration.Tests/ExpiryPolicyTests.cs`, `ExpiryPolicyConcurrencyTests.cs`; existing `tests/backend/unit/Intelibill.Application.Unit.Tests/Infrastructure/Repositories/InventoryBatchRepositoryTests.cs`, sales service/preview/record and inbound processor suites.

**Interfaces:**
- `ExpiryApprovalRequest(Guid InventoryBatchId, decimal Quantity, uint BatchVersion, long PolicyVersion, long CatalogVersion, string Reason)`; optional IReadOnlyList on RecordSaleCommand.
- `SaleExpiryGuard.EvaluateAsync(Guid shopId, DateOnly businessDate, IReadOnlyList<ValidatedSaleLine> lines, CancellationToken ct) -> Task<IReadOnlyList<SaleExpiryLineDecision>>` for previews.
- `SaleExpiryGuard.AuthorizeAsync(Guid shopId, Guid actor, DateOnly businessDate, IReadOnlyList<ValidatedSaleLine> lines, IReadOnlyList<ExpiryApprovalRequest>? approvals, CancellationToken ct) -> Task<ErrorOr<IReadOnlyList<SaleExpiryDecision>>>` for posting under fence. Sale attaches resulting immutable evidence once.
- `BatchExpiryRevisionService` validates/captures changed dates and advances catalog under exclusive fence; use `ReviseAsync(InventoryBatch batch, DateOnly? expiryDate, DateOnly? manufacturingDate, Guid actor, string reason, CancellationToken ct) -> Task<ErrorOr<Success>>`. Caller owns version-check/transaction/save. Date-only edits must reach this service too. UpdateBatchPrices passes existing Reason; legacy UpdateInventoryBatch passes Notes. Require nonzero ExpectedVersion when either date changes, even on the legacy route; price-only edits keep existing rules.
- Batch exposes QuarantinedQuantity and UnquarantinedQuantity; sale consumption checks unquarantined quantity cumulatively. Task 4 adds origin-specific changes.

- [ ] **Write failing integration scenarios.** `ExpiryBoundary_UsesShopBusinessDate` checks `2026-10-21T18:29:59Z` versus `18:30:00Z` for Asia/Kolkata across barcode/search/preview/post. `DirectBatchId_CannotBypassExpiry`, `MissingRequiredExpiry_BlocksWithSpecificReason`, `HardBlock_CannotBeOverridden` assert no sale/number increment/stock/ledger/till effects. `WarningOverride_RequiresRoleReasonAndAudit` covers Staff, forged actor/boolean, blank reason, wrong versions and exact persisted actor/reason. `DuplicateLines_RequireSummedApprovalQuantity` covers the same batch twice and duplicate approval entries. `Fefo_OrdersEligibleLotsWithoutChangingQuantities` checks dated ascending, undated last, stable ID tie break, blocked diagnostic rows disabled. Receipt tests cover each entry path and bulk mixed success with failed row zero writes. Date edit writes audit even when prices unchanged.
- [ ] **Run red.** `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter 'FullyQualifiedName~ExpiryPolicyTests|FullyQualifiedName~ExpiryPolicyConcurrencyTests'`.
- [ ] **Implement shared projections and guard.** Keep administrative product/intake details visible; default available endpoint excludes blocked stock, optional includeUnavailable shows reasons. Add policy fence after period and before till in online posting; load/reload policy/date facts within it. Preview uses the same business clock and returns structured decisions without requiring approval to inspect a warning. Authorize before number/stock/till mutation; include new payloads only in the new optional hasher branch. Never call live expiry validation from the offline path: Task 3 supplies its own trusted context. Existing stock validation remains shared.
- [ ] **Implement intake/date revision rules and migration.** Wrap every affected date/receipt writer in correct fence order; do not start nested transactions. Enforce manufacturing<=expiry and required expiry before writing a row. Add batch date history and immutable sale evidence. Preserve original invoice JSON and old null-field hash fixtures. No expiry mutation releases quarantine.
- [ ] **Verify green including concurrency.** A PostgreSQL barrier holds a sale's shared fence while an owner policy/date edit waits; inverse ordering makes sale see new Block. Assert lock timeout/restoration and no deadlocks with period/till/customer locks. Run focused tests, existing RecordSale/PreviewSale/Inbound unit tests and `dotnet build src/backend/Intelibill.slnx`.
- [ ] **Commit scoped files.** `feat: enforce expiry on online sales`.

### Task 3: Preserve trusted offline issue-time authority

**Files:**
- Create: `Intelibill.Domain/Entities/OfflineExpiryEvidence.cs`, `Intelibill.Domain/Interfaces/Repositories/IOfflineExpiryEvidenceRepository.cs`, `Intelibill.Infrastructure/Repositories/OfflineExpiryEvidenceRepository.cs`, matching EF configuration/migration `AddOfflineExpiryEvidence`.
- Create: `Intelibill.Application/Features/Expiry/OfflineExpiryAuthority.cs`.
- Modify: `Intelibill.Application/Features/OfflineSalesSnapshot/Services/OfflineSalesSnapshotStreamingService.cs`, `DTOs/OfflineSalesSnapshotStreamRecords.cs`; `Intelibill.Application/Features/Sales/Commands/SyncOfflineSales/SyncOfflineSalesCommand.cs`, `OfflineSalePoster.cs`, `IOfflineSalePoster.cs`, `OfflineSaleSyncIdempotencyHasher.cs`, `OfflineSubmissionPayload.cs`, `OfflineStockConsumption.cs`.
- Modify: `Intelibill.Domain/Enums/ReconciliationIssueType.cs`; `Intelibill.Application/Features/OfflineReconciliation/ReconciliationActionPolicy.cs`, `ReconciliationCaseMapper.cs`, `Commands/ResolveReconciliationCase/ResolveReconciliationCaseCommandHandler.cs`; shared DTOs and payload validators.
- Test: `tests/backend/integration/Intelibill.Integration.Tests/ExpiryOfflineTests.cs`; snapshot and sync unit suites; `tests/contracts/expiry-policy-v1.json` new C#/TS decision cases.

**Interfaces:**
- `OfflineExpiryEvidence` retains EvidenceId, SnapshotId, ShopId, ItemId, BatchId, CapturedAt, ExpiresAt, CatalogVersion, EffectivePolicy, BatchFacts; immutable after creation.
- `OfflineExpiryLineEvidence(Guid EvidenceId, long CatalogVersion, long PolicyVersion, DateOnly BusinessDate, DateOnly? ExpiryDate, string DecisionCode)` optional on each offline goods line; values are checked against the server record, never accepted as authority.
- `OfflineExpiryAuthority.ValidateAsync(OfflineSalePostingRequest request, IReadOnlyList<ValidatedSaleLine> lines, CancellationToken ct) -> Task<ErrorOr<IReadOnlyList<SaleExpiryDecision>>>`; the poster maps failures to held ExpiryReview=14 except transient infrastructure/lock errors, which retain retry behavior. Authenticated attribution and existing lease gates remain required.
- Catalog schema 4 contains time zone/catalog version and per-lot evidence; published rows must already exist. Bounded materialized pages avoid reads and writes on one active Npgsql reader.

- [ ] **Write failing historical-proof tests.** `OfflineIssueBeforeExpiry_SyncAfterExpiry_PreservesIssueDecision` posts unchanged amounts/number using retained policy/date facts. `OfflinePolicyUnknown_RequiresReview` asserts envelope retained and no sale/stock/till/ledger posting. Cover cross-tenant/replaced batch ID, forged client policy/date/result, SoldAt before capture/after proof expiry, future clock/lease mismatch, partial new fields, forged warning approval, relevant edit before versus after issue, old Ignore before first activation and changed-back Ignore after activation. Repeat through ordinary sync, recovery submission, RetryHistoricalLease and ReconcileTillSale: none bypass expiry. Add valid issue with current quarantined stock => no quarantine consumption and stock variance.
- [ ] **Run red.** `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~ExpiryOfflineTests`.
- [ ] **Implement evidence capture, validation and held cases.** Acquire consistent policy fence for each catalog capture, persist facts before exposing ID; complete metadata marks catalog active only after full success. Compute revision validity from relevant shop/item policy and date histories (unrelated catalog version change alone does not invalidate historical issue). Preserve absent-field legacy hashes/envelopes exactly. Held retries re-run authority; no manager-reason bypass. ExpiryReview permits Escalate, exact-envelope LinkExistingSale only after verifying the posted sale's retained expiry authority, and RetryHistoricalLease/ReconcileTillSale only when complete trusted expiry evidence passes (plus existing attribution/till prerequisites). Never offer LinkStockCorrection or AcknowledgeVariance. Check these conditions in both action projection and execution; test crafted forbidden actions and stale offered actions cannot resolve the case. Attach successful historical decisions to posted sale. Register new table RLS and held type in all API/console mappings.
- [ ] **Verify green and contracts.** Test a truncated stream does not activate partial catalog, retained facts survive policy/date edits, new evidence cannot be rewritten by application API, and idempotent replay returns original decisions after expiry. Run `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter 'FullyQualifiedName~Offline|FullyQualifiedName~Reconciliation'` and focused integration tests.
- [ ] **Commit scoped files.** `feat: verify offline expiry evidence`.

### Task 4: Restore and dispose physical quarantine safely

**Files:**
- Create: `Intelibill.Domain/Entities/BatchDispositionChange.cs`, `Intelibill.Domain/Interfaces/Repositories/IBatchDispositionRepository.cs`, `Intelibill.Infrastructure/Repositories/BatchDispositionRepository.cs`, matching EF configuration/migration `AddBatchDispositionEvidence`.
- Create: `Intelibill.Application/Features/Expiry/BatchDispositionService.cs`, `BatchDispositionContracts.cs`; API `Intelibill.Api/Controllers/BatchDispositionsController.cs`.
- Modify: `Intelibill.Domain/Entities/InventoryBatch.cs`, `SaleReturnItem.cs`, `SaleCancellationLine.cs`, `InventoryAdjustment.cs`; enum `FinancialOperationType.cs` append BatchDisposition=27; configurations and operation lookup mapping.
- Modify: `Intelibill.Application/Features/Sales/Services/Returns/SaleReturnValidator.cs`, `ISaleReturnValidator.cs`; `Commands/RecordSaleReturn/RecordSaleReturnCommandHandler.cs`, `Commands/VoidSaleReturn/VoidSaleReturnCommandHandler.cs`; `Services/Cancellations/SaleCancellationService.cs`, cancellation DTOs and return DTOs in the Sales feature.
- Modify: Inventory command handlers `CorrectStockCount/CorrectStockCountCommandHandler.cs`, `CreateAdjustment/CreateAdjustmentCommandHandler.cs`, `VoidAdjustment/VoidAdjustmentCommandHandler.cs`, `VoidBatch/VoidBatchCommandHandler.cs`; SupplierReturns services `SupplierReturnService.cs`, `PurchaseReceiptReversalService.cs`; batch/inventory projections and stock-valuation readers.
- Test: `tests/backend/integration/Intelibill.Integration.Tests/ExpiryDispositionTests.cs`, `ExpiryDispositionConcurrencyTests.cs`; existing `ReturnAccountingExportTests.cs`, `SaleCancellationReportingTests.cs`, `StockCorrectionInvariantTests.cs`, `SupplierReturnWorkflowTests.cs`.

**Interfaces:**
- `RestorationDisposition(decimal PhysicalQuantity, decimal QuarantinedQuantity, string? ReasonCode)` computed by `BatchDispositionService.EvaluateRestoration(ExpiryBatchFacts batch, ExpiryPolicy policy, DateOnly date, decimal quantity)`; known expired goods quarantine even under Ignore.
- `StageRestorationAsync(Guid shopId, Guid actor, InventoryBatch batch, string sourceKind, Guid sourceId, Guid sourceLineId, Guid stockMovementId, RestorationDisposition disposition, DateOnly businessDate, CancellationToken ct) -> Task<ErrorOr<Guid?>>` returns root change ID or null; caller owns transaction/save and total inventory/movement.
- `StageReversalAsync(Guid shopId, Guid actor, InventoryBatch batch, Guid rootChangeId, decimal quantity, Guid reversalMovementId, DateOnly businessDate, CancellationToken ct) -> Task<ErrorOr<Success>>`; refuses roots with subsequent release/disposal.
- `BatchDispositionRequest(string OperationKey, uint ExpectedVersion, Guid QuarantineChangeId, string Action, decimal Quantity, string Reason)`; actions Release/Dispose only, operation 27. Response freezes change/root IDs, physical/quarantine balances and resulting version.
- Domain methods explicitly distinguish unquarantined subtraction, quarantine restoration, release and quarantine disposal. No public setter or generic subtraction may violate subset conservation.

- [ ] **Write failing physical/accounting tests.** `ExpiredCustomerReturn_IsQuarantinedNotSellable`: original 5 physical, return2 expired =>7 physical/2 quarantine, return movement+2, available excludes expired units, no extra stock-loss posting. Add partially quarantined unexpired lot=physical7/quarantine2/available5. `ReturnVoid_ReversesOriginalQuarantine` yields original5/0; void after root release/disposal fails. `CancellationRestore_UsesSameDisposition` preserves cancellation eligibility/document/till evidence. Disposal2 => physical5/quarantine0 and exactly one recorded-cost stock loss; release leaves physical unchanged. P&L/export reverses return COGS once and recognizes disposal once on disposal date.
- [ ] **Run red.** `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter 'FullyQualifiedName~ExpiryDispositionTests|FullyQualifiedName~ExpiryDispositionConcurrencyTests'`.
- [ ] **Implement root journal and restoration integration.** Replace expired-Restockable rejection with effective disposition in preview and posting; keep explicit Wastage/service semantics. Freeze root reference/quarantined amount on return/cancellation lines. Fence before till/customer/sale/batch acquisition; reuse existing source transaction/coordinator instead of nested saves. Void consumes only original available source disposition; wrong shop/root/source cannot pass. Add check constraint `0 <= quarantined_quantity AND quarantined_quantity <= quantity`.
- [ ] **Implement audited owner release/disposal and protect every stock writer.** Durable executor authorizes before replay and checks xmin/root balance/scale/reason. Release originally expired root fails even after date extension; missing-date root release needs audited date correction and current clean eligibility. Disposal uses linked independent adjustment/movement; normal adjustment void refuses disposal sources. Counts below quarantine fail; decreases and supplier shipments use unquarantined balance; receipt reversals refuse quarantine history. Generic increase/void does not reset quarantine. Publish history and physical/quarantine/value projections.
- [ ] **Verify green with concurrency and conservation.** Two simultaneous disposition/void/sale operations cannot drive root or aggregate negative. Test identity, source replay, changed payload, lost authority before replay, out-of-shop root, fractional quantities and closed period. Assert original legacy return/cancellation/till tests still pass. Run focused integration plus `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests` and related application suites.
- [ ] **Commit scoped files.** `feat: quarantine recovered expired stock`.

### Task 5: Complete web administration, selling and recovery flows

**Files:**
- Create: `features/expiry/services/expiry-policy.service.ts`, `expiry.models.ts`, `expiry-policy-evaluator.ts`, `batch-disposition-draft.service.ts`; `features/expiry/components/expiry-policy-form.component.ts`, `batch-disposition-dialog.component.ts` and associated templates/specs.
- Modify: `features/shops/components/manage-shop-overlay.component.ts/.html`; `features/inventory/components/edit-item-overlay.component.ts/.html`, `add-product-overlay.component.ts/.html`, `batch-page/batch-row-form.component.ts/.html`, `batches-list/batches-table.component.ts/.html`; `features/inventory/services/inventory.models.ts` and service API mappings.
- Modify: `features/sales/components/new-sale/batch-picker-dialog.component.ts/.html`, `cart-table.component.ts/.html`, `sale-confirmation-dialog.component.ts/.html`; `features/sales/pages/new-sale-page.component.ts/.html`, `new-sale-page.offline-flow.service.ts`; `features/sales/services/sale.models.ts`, `sale.service.ts`, `sale-preview.service.ts`, `sale-cart-state.service.ts`, `checkout-intent.service.ts`.
- Modify: `features/sales/services/offline-sale-finalization.service.ts`, `offline-sale-state.service.ts`, `offline-sale-core.types.ts`, `offline-sales-snapshot-sync.service.ts`, `offline-sales-queue-sync.service.ts`; `features/sales/utils/offline-sales-snapshot.schema.ts`, `offline-sale-sync.mapper.ts`, `offline-recovery-bundle.ts`; `core/storage/offline-sales-snapshot-indexeddb.service.ts`.
- Modify: `features/sales/components/sale-detail/sale-return-preview-dialog.component.ts/.html`; `features/sales/pages/offline-reconciliation-page/offline-reconciliation-page.messages.ts` and template; `features/sales/services/offline-reconciliation.models.ts`; all shipped `src/frontend/public/assets/i18n/*.json`.
- Test: colocated component/service specs; evaluator consumes `tests/contracts/expiry-policy-v1.json`; queue/finalization/checkout-intent and scope transition regressions.

**Interfaces:**
- TS ExpiryPolicy/SellabilityDecision mirror Task 1 DTO fields and enum names; `evaluateExpiry(batch,policy,catalogVersion,businessDate)` exactly matches contract fixtures. `businessDate` is YYYY-MM-DD in server-provided shop zone, never `toISOString().slice(0,10)`.
- Form submits Task 1 expected-version/reason contracts. Checkout sends Task 2 per-batch approval bindings. Queue freezes Task 3 per-line evidence. Disposition dialog uses Task 4 action/root/version contract and durable owner/shop/action-target claims.

- [ ] **Write failing user-flow tests.** Owner edits shop/item policy and sees version/readiness; Staff cannot edit or approve. Picker stable FEFO, undated last, blocked reason accessible, no blocked Add action, quarantined subset not sold. Manager warning reason creates exact binding; cart/quantity/version change clears unsubmitted binding. Local midnight invalidates stale available state. Receipt required date/order errors localize. Return preview explains quarantine and saved state. Release/disposal unknown HTTP and lookup404 retain original payload/key across reload/tab/shop switches.
- [ ] **Run red.** From `src/frontend`: `bun run test --watch=false --include='**/expiry/**/*.spec.ts'` and affected existing specs using Angular runner's include option. If runner syntax differs, use its established targeted-test form without weakening assertions.
- [ ] **Implement settings and online flows.** Add focused reusable policy form, inventory history/disposition UI and checkout warning panel. Integrate existing state/services rather than parallel carts. Show policy/time zone/version only where useful for operator decisions; hide implementation IDs except audit details. Every status has text/accessible label and all locale keys. Freeze online approval data through existing checkout intent; unknown submitted outcomes never get new keys.
- [ ] **Implement offline schema/evidence preservation.** Accept schema4 as active only when complete; keep schema1..3 decoding for issued invoices. New finalization requires supported authority, calculates at issue business date, and refuses server-only warning approval. Do not upgrade queued payloads by injecting current evidence. Carry evidence through sync/recovery/checksum/hash/backup unchanged. Policy edits invalidate future-use snapshot and unsubmitted preview caches; do not delete pending invoices, shadow-stock reservations or print documents. Show actionable ExpiryReview/refresh/online-approval messages.
- [ ] **Verify green.** Run affected specs then `bun run test --watch=false`, `bun run build`, `bun run audit:ui:release`. Use existing local UI harness/browser smoke to inspect shop policy, lot statuses, warning reason, receipt date errors, quarantine return/history and a disposition conflict on desktop/mobile-width web. Capture material layout defects and fix before commit. Preserve current till source selectors and settlement evidence in regression tests.
- [ ] **Commit scoped files.** `feat: expose expiry policy and quarantine flows`.

### Task 6: Close migration, invariants and release verification

**Files:**
- Create: `docs/expiry-aware-selling-rollout.md`, `scripts/db/audit-expiry-readiness.sql` (read-only, tenant scoped by explicit input).
- Modify: `docs/backend-architecture.md`, `docs/frontend-architecture.md`, `AGENTS.md`, applicable migration/tenant/schema test expectations and this plan's completion boxes.
- Test: `tests/backend/integration/Intelibill.Integration.Tests/ExpirySchemaTests.cs`; new and existing acceptance fixtures from earlier tasks.

**Interfaces:** None new. This task verifies the assembled contract and records exact generated migration IDs/deployment steps.

- [ ] **Add/verify upgrade tests.** Upgrade a populated pre-feature database, assert unchanged physical quantity/date/invoice JSON and default Ignore, zero quarantine, bounded constraints and restricted-role RLS on every new table. Assert destructive Down refuses retained evidence removal. Verify new shop bootstrap. Readiness SQL reports missing/expired/quarantined facts without UPDATE/DELETE; API totals match.
- [ ] **Write rollout and architecture documentation.** List ordered real migration IDs and all-writers-before-enablement requirement. State nonregulated assumptions, inclusive date semantics, offline stale-policy limit, no offline warning approval, held legacy uncertainty, owner policy change/release/disposal authority, recorded-cost quarantine limits and immutable evidence retention. Explain no downgrade to pre-feature writers after facts, no automatic production enablement, no dates invented and no quantity migration. Document operator recovery without direct DB edits.
- [ ] **Run final required checks after fixes.** `dotnet build src/backend/Intelibill.slnx`; `dotnet test src/backend/Intelibill.slnx` with Docker available. From `src/frontend`: `bun run test --watch=false`, `bun run build`, `bun run audit:ui:release`. Run repository schema/release checks named by current scripts if not already included. Do not overlap integration/build jobs that contend for generated outputs or Docker fixtures. Record exact passed/failing counts and any baseline failures; never call known failures passing.
- [ ] **Review complete branch independently and fix findings.** Review against this spec, actual diff and tests: hard-block bypass, policy fence lock order, legacy/null hash compatibility, evidence authority, quarantine conservation and accounting dates, all source writers, tenancy/replay authorization, web unknown intents. Run focused regressions for fixes and broader checks only when their scope requires it. The coordinator selects reviewers per the user's requested model/workflow.
- [ ] **Commit only scoped final files and open PR.** Confirm `git diff --check`, staged scope, clean worktree and correct branch/base. PR describes actual behavior, conservative defaults, migrations and verified tests/limitations. No deployment, main merge or pilot enablement is implied. Title suggestion: `Add expiry-aware selling and return quarantine`.

## Self-review of this plan

All supplied scope maps to a task: policy/history/readiness (1); search/barcode/FEFO/preview/final authorization/intake date consistency (2); trusted issue-time offline validation and legacy review (3); physical quarantine, release/disposal/void/cancellation and writer invariants (4); UI/i18n/durable flows (5); migration/RLS/rollout/rollback/final review (6). Warning and hard-block semantics are identical across domain, application and web; offline authority is explicitly distinct from current online policy. Quarantine remains a quantity subset with root provenance and separate disposal economics. No approval pause is introduced because automatic execution was explicitly authorized.

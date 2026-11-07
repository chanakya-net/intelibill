# Purchase receiving lots and error recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recover receiving work safely and receive multiple physical lots against one PO line with one atomic, replayable receipt.

**Architecture:** Extend the existing financial-operation executor with PO receipt replay and lookup. Validate grouped lots under the existing PO lock, preserve stock/payable/receipt atomicity, and store client lineage. A dedicated owner/PO-scoped IndexedDB record holds draft and frozen operation together; correlated NgRx outcomes confirm only their own dialog.

**Tech Stack:** ASP.NET Core 10, EF Core/PostgreSQL, ErrorOr, FluentValidation, Wolverine; Angular 21, NgRx, PrimeNG, Transloco, IndexedDB, Bun; xUnit/Testcontainers, Vitest and Playwright.

**Spec:** [Refreshed design and dependency audit](../specs/2026-11-07-purchase-receiving-lots-recovery-design.md), based on the user's supplied October implementation plan/backlog 30 and current main `f9b26edc77eb8ea825d2aabf63a98670faf735d7`.

## Global Constraints

- Angular web and .NET API for Indian retail public SaaS; mobile excluded, existing mobile request compatibility preserved.
- Preserve Domain ← Application ← Infrastructure ← Api layering, repository abstractions and IUnitOfWork writes; no exception-driven business control flow.
- Frontend uses Bun only; C# warnings remain errors; migrations run separately from API startup.
- Keep the existing whole-unit PO contract. Item quantity policy still applies; no fractional PO lifecycle expansion.
- Keep `taxIncluded` and `purchaseTaxIncluded` separate and wire-compatible. Do not infer tax recovery from either flag.
- One receipt is atomic. Do not use `IInboundRowExecutor` or partial-success bulk intake behavior.
- Supplier returns do not exist on current main; preserve lineage without adding their implementation or dead UI actions.
- The user explicitly authorized autonomous Astra planning followed by Sol implementation, review/fixes and PR. This plan needs no further approval gate; the parent controls implementation/review handoff.
- Work in the already-created `codex/purchase-receiving-lots-recovery` branch. No unrelated repository cleanup.

## Review Focus

- **RF1:** Validation/network/storage failure preserves every field; only a matching confirmed operation closes/clears; uncertain requests survive reload and scope transitions.
- **RF2:** Repeated PO line IDs receive distinct lots, with grouped quantities validated against locked remaining stock before mutation.
- **RF3:** Lost responses, same-key concurrency, final-quantity races and PO/period status changes converge on one committed receipt/payable and remain recoverable.
- **RF4:** Duplicate identities, foreign items/lines and invalid cost/tax yield row errors and no partial batch/stock/payable/receipt writes.
- **RF5:** Selling from a lot and later correcting its receipt cost preserves original receipt snapshots and source IDs; supplier-return behavior is not claimed.

---

## Verified baseline and task order

Parent verified the clean base before implementation: 81 purchase-order Application unit tests, 40 real-PostgreSQL PurchaseOrdersController integration tests, and 139 frontend purchase-order tests across 12 files pass. Docker is available; installed SDK 10.0.403 is accepted by global roll-forward. These are baseline results, not evidence for this change.

Three sequential reviewable tasks: backend capability first; complete durable UI second; end-to-end evidence and release verification third. Each task has its own red/green cycle and commit. Do not commit generated test reports, local database secrets or build outputs.

### Task 1: Atomic multi-lot receipt, stable replay and lineage

**Files to modify:**

- `src/backend/Intelibill.Api/Controllers/PurchaseOrdersController.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommand.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommandValidator.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommandHandler.cs`
- `src/backend/Intelibill.Application/Common/Errors/Errors.PurchaseOrder.cs`
- `src/backend/Intelibill.Domain/Enums/FinancialOperationType.cs`
- `src/backend/Intelibill.Application/Features/FinancialOperations/FinancialOperationTypes.cs`
- `src/backend/Intelibill.Domain/Entities/PurchaseOrderReceipt.cs`
- `src/backend/Intelibill.Domain/Entities/PurchaseOrderReceiptLine.cs`
- `src/backend/Intelibill.Infrastructure/Data/Configurations/PurchaseOrderReceiptLineConfiguration.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/DTOs/PurchaseOrderDtos.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Services/PurchaseOrderDtoMapper.cs`

**Create:**

- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/PurchaseOrderReceiptPayload.cs` — canonical hash payload, not transaction ownership.
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/PurchaseOrderReceiptErrors.cs` — row metadata helper using ErrorOr, with no HTTP dependency.
- Generated `src/backend/Intelibill.Infrastructure/Migrations/<timestamp>_AddPurchaseReceiptClientLineIdentity.cs`, designer and model snapshot update.
- `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommandValidatorTests.cs`

**Tests to extend:**

- `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommandHandlerTests.cs`
- `tests/backend/unit/Intelibill.Api.Unit.Tests/Controllers/PurchaseOrdersControllerTests.cs`
- `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/PurchaseOrderTests.cs`
- `tests/backend/integration/Intelibill.Integration.Tests/PurchaseOrdersControllerTests.cs`
- `tests/backend/integration/Intelibill.Integration.Tests/FinancialOperationTests.cs`

**Interfaces produced:**

- Append `Guid? OperationId = null` to receipt request/command and `Guid? ClientLineId = null` to line request/input. Preserve positional source compatibility by appending optional parameters.
- `PurchaseOrderReceiptPayload.From(ReceivePurchaseOrderCommand command)` returns the canonical hash record described in the design. `FinancialOperationHasher.Compute(...)` remains the shared hasher; do not change its global version for this new operation type.
- Keep `HandleAsync(ReceivePurchaseOrderCommand, CancellationToken) : Task<ErrorOr<PurchaseOrderDetailDto>>`; extract `ReceiveCoreAsync(command, receivedAt, shop, cancellationToken)` as the transaction-body method, or equivalent private method with explicit inputs.
- Append nullable/defaulted `ClientLineId` to `PurchaseOrderReceiptLineDto`; mapper returns `line.ClientLineId ?? line.Id`. Append optional client ID to `PurchaseOrderReceipt.AddLine` and its factory.
- Receiving business-error metadata: `clientLineId` (when supplied), `lineIndex` (zero based), `field` (wire field name). `PurchaseOrderReceiptErrors.ForLine(Error error, Guid? clientLineId, int lineIndex, string field)` preserves error type/code/description and adds only those fields.
- Endpoint adds `rowErrors: [{clientLineId?, lineIndex?, field?, code, description}]` to the existing ProblemDetails for handler failures. Use a local receiving projection/helper in the controller; do not serialize all Error.Metadata globally. FluentValidation paths remain compatible and are normalized client-side in Task 2.
- Lookup slug `purchase-order-receipt` maps to enum 13 and uses the existing `FinancialOperationDto` with `targetId`, `operationKey` and `response`. No new operation storage table or lookup controller is needed.

- [ ] **Write red unit/contract tests.** Assert duplicate PO IDs are allowed, duplicate explicit client IDs/empty UUID are rejected, absent IDs remain accepted, grouped remaining 10 rejects 6+6 before any processor call, and one row's client identity is retained through mapping. Cover quantity > 0 and integral wire quantities; negative costs/MRP/sales price, sales price > MRP, tax outside 0..100, blank/oversized barcode and batch fields. Verify independent tax flags and harmless repeated product barcode. Add hash tests showing each business field changes the hash, numeric scale/trimmed text does not, and omitted `receivedAt` stays stable.
- [ ] **Run red tests.** From repo root: `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter FullyQualifiedName~ReceivePurchaseOrder` and `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter FullyQualifiedName~PurchaseOrdersControllerTests`. Confirm failures reflect the missing contract/behavior.
- [ ] **Implement the additive contract, canonical hash and replay boundary.** Authorize an active Owner/Manager before execution. If keyed, call `IFinancialOperationExecutor.ExecuteAsync` with scope `(shop, PurchaseOrderReceipt, operationId.ToString("D"))` and target PO; callback saves but never begins/commits/rolls back its own transaction. If unkeyed, retain an explicit legacy transaction around the same body. On errors/throws, rollback and clear tracked mutations. Take the existing posting-period fence inside the body, then existing PO/line locks; replay skips these business eligibility checks. Persist the original detail response for exact replay.
- [ ] **Implement all-lot preflight and row errors.** Resolve every input PO line and shop-owned item under the lock; group summed quantities using `long`/`decimal` before comparisons. Call `QuantityPolicy.Validate` for each item. Reject duplicate trimmed item/batch identities and identify all affected rows. Preserve current batch matching case semantics and barcode behavior. Validate scalar fields before inbound processing; a processor error is decorated with the failing row metadata. Only after complete preflight invoke existing processor, link receipt values, apply each lot quantity, and save once. Do not change unrelated inventory behavior.
- [ ] **Implement lineage and generate the migration.** Add nullable column/backfill/index exactly as the design specifies; new omitted IDs fall back to receipt-line ID. Run `dotnet ef migrations add AddPurchaseReceiptClientLineIdentity --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api`. Inspect SQL for only the intended additive column/index/backfill and no cost/quantity changes. Keep old-server null writes supported. Register the new operation type/slug without renumbering existing types.
- [ ] **Write real-PostgreSQL tests (RF2–RF4).** `SamePoLine_TwoLots_ReceivesGroupedTotal`: remaining10, A4 costing40 + B6 costing90 -> one receipt/two receipt lines/two batches/two stock movements/stock10/receipt values130/payable130. `GroupedOverRemaining_RejectsAllRowsWithoutWrites`: A6+B6 -> no new rows or PO changes. `SecondLotProcessorFailure_RollsBackEverything`: force a later inbound failure after the first row has been staged, then assert receipts, batches, stock, receipt values, supplier ledger, inventory totals and operation receipt all unchanged. `SameKeyConcurrent_PostsOnce`: independent request scopes share one key and body and get identical receipt IDs; assert accounting counts and sums. `DifferentKeys_FinalQuantity_OnlyOnePosts`: competing keys cannot overreceive. `LostResponse_ReplayAndLookup_AfterPoAndPeriodClose`: original key returns original response even after current eligibility changes. `SameKeyChangedPayload_Conflicts`, `CrossShopKey_DoesNotLeak`, `StaffOrRevokedMember_CannotReplay`, and `LegacyRequestWithoutIds_StillReceives`. Assert duplicate trimmed lot, duplicate client ID, foreign PO line/item and invalid cost/tax responses have row/index errors and no effects. Use actual transactions, not repository mocks, for atomicity/concurrency evidence.
- [ ] **Run green backend tests.** `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter FullyQualifiedName~PurchaseOrders`; `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter FullyQualifiedName~PurchaseOrdersControllerTests`; `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter FullyQualifiedName~PurchaseOrder`; `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter 'FullyQualifiedName~PurchaseOrdersControllerTests|FullyQualifiedName~FinancialOperationTests'`. Expected: all pass and real database assertions prove one posting/atomic rejection.
- [ ] **Review and commit the backend capability.** Include migration and contract tests. Suggested commit: `feat: make purchase receipts atomic and replayable`.

### Task 2: Durable drafts and correlated multi-lot UI

**Files to create:**

- `src/frontend/src/app/features/purchase-orders/services/receiving-draft.service.ts` and `.spec.ts` — durable IndexedDB record, prepare/settle/CAS lifecycle.
- `src/frontend/src/app/features/purchase-orders/services/receiving.models.ts` — persisted draft, operation, correlation and normalized error types.
- `src/frontend/src/app/features/purchase-orders/services/receiving-errors.ts` and `.spec.ts` — normalize existing validator paths and new row errors; classify outcomes.

**Files to modify, with existing specs:**

- `src/frontend/src/app/features/purchase-orders/services/purchase-order.service.ts`
- `src/frontend/src/app/features/purchase-orders/state/purchase-orders.actions.ts`, `.effects.ts`, `.reducer.ts`, `.selectors.ts`, `.facade.ts`
- `src/frontend/src/app/features/purchase-orders/components/receive-purchase-order-dialog.component.ts`
- `src/frontend/src/app/features/purchase-orders/pages/purchase-order-detail-page.component.ts` and `.html`
- `src/frontend/src/app/features/purchase-orders/pages/purchase-orders-list-page.component.ts`
- `src/frontend/src/app/features/purchase-orders/components/purchase-order-receipt-history.component.ts` and new `.spec.ts`
- All nine existing JSON locale files in `src/frontend/public/assets/i18n/`; extend `features/purchase-orders/i18n-coverage.spec.ts`.

**Interfaces consumed/produced:**

```ts
type ReceivingCorrelation = {
  readonly scope: ShopScope; // existing userId, shopId, epoch
  readonly purchaseOrderId: string;
  readonly operationId: string;
};
type ReceivingRowError = {
  readonly clientLineId?: string;
  readonly field?: string;
  readonly code: string;
  readonly messageKey: string;
};
// Persisted record: schemaVersion:1, userId, shopId, purchaseOrderId,
// revision:number, editable draft (all fields and stable client IDs),
// pending:null | {operationId, payload, status:'sending'|'unknown'},
// confirmedOperationId:string|null, updatedAt:string.
```

- `ReceivingDraftService.load(scope: ScopeOwner, poId: string): Promise<ReceivingDraftRecord | null>`.
- `save(scope: ScopeOwner, poId: string, draft: ReceivingEditableDraft, expectedRevision: number): Promise<ReceivingDraftRecord>` rejects stale revisions and cannot replace pending/confirmed operations.
- `prepare(scope: ScopeOwner, poId: string, draft: ReceivingEditableDraft, expectedRevision: number): Promise<ReceivingDraftRecord>` atomically freezes payload/UUID before returning; returns/refuses an existing pending operation without making a fresh key. It must not send anything.
- `markUnknown(scope, poId, operationId): Promise<void>` and `settle(scope, poId, operationId, outcome: 'confirmed' | 'rejected'): Promise<void>` compare the stored operation ID before updating. Confirmation persists a tombstone before cleanup; rejection retains editable fields. Implement a small clear/discard method only for nonpending drafts and confirmed cleanup.
- `PurchaseOrderService.receivePurchaseOrder(poId, payload, owner?: ScopeOwner)` attaches `scopedRequestContext(owner)` for new sends; optional owner preserves existing callers/tests. `getReceiptOperation(operationId, owner)` calls the existing financial lookup with the new slug and scoped request context.
- Receive-request, success and failure actions carry the same `ReceivingCorrelation`; request also carries frozen payload, failure carries outcome (`rejected`/`unknown`) and `ReceivingRowError[]` plus a top-level translation key. Add correlated check-operation actions with lookup-miss distinct from failure. The facade exposes operation result/state for one correlation, not global `submitting` as a receipt-completion signal.

- [ ] **Write red durable-state tests (RF1/RF3).** Reload retains values and client IDs. Prepare resolves only on transaction complete; abort/error/unavailable storage sends nothing. Pending replays use byte-equivalent business payload/IDs, even if PO defaults changed. Two prepares/stale saves cannot replace one pending operation; another user/shop/PO reads none of it. Matching confirmation clears/tombstones; unmatched success does nothing; rejected keeps values; unknown and payload mismatch stay frozen. Failed cleanup never permits a new operation from an old confirmed draft.
- [ ] **Write red effect/page/dialog tests (RF1/RF2).** Both launch pages remain open after dispatch and validation/network failure; unrelated success/PO/epoch cannot close. Reload/reopen of a Received PO with a pending request exposes recovery. Scope transition rejects a foreign send and retains a late response as unknown under its original owner. `prepareToLeave()` refuses in-flight work and flushes drafts. Group A4+B6 is valid; A6+B6 is invalid; add/remove lots preserves IDs/errors; all form fields survive order refresh. Async defaults completing after edits/restoration/removal cannot mutate the wrong row or frozen payload.
- [ ] **Run red frontend tests.** In `src/frontend`: `bun run test --run 'src/app/features/purchase-orders/**/*.spec.ts'`. Confirm behavior assertions fail before implementation.
- [ ] **Implement the durable record and error adapter.** Use a dedicated IndexedDB database/store, compound key `[userId,shopId,purchaseOrderId]`, and revision checks inside readwrite transactions. Resolve saves on `transaction.oncomplete`; reject abort/error and absent IndexedDB. Store dates as plain strings/null; persist all form fields, even invalid editable input. Rehydrate form dates without timezone shifting. Map validator `Lines[n].Field` codes against frozen row order, and handler rowErrors by client ID/index; never parse human messages. Reuse existing failure classification semantics but retain `Operation.PayloadMismatch` for lookup. No shared financial-intent localStorage schema changes are required.
- [ ] **Implement correlated effects and scope ownership.** Persist/prepare before dispatch; use `beginMutation`, scoped HTTP contexts and matching epochs. Serialize receives without `switchMap` cancellation; a per-operation in-flight guard plus `mergeMap` is suitable (duplicates ignored, independent targets allowed). Settlement occurs under captured owner before emitting success/failure and releasing mutation tracking. Lookup verifies operation/target IDs; 404 leaves unknown. Stale scope results cannot repopulate reset NgRx state. Confirmed operation responses trigger live detail/list refresh after confirmation; they must not overwrite newer PO state as authoritative.
- [ ] **Implement multi-lot/recovery UI in both entry points.** Remove immediate close. Initialize once for owner/PO and restore saved draft; same-order refresh updates remaining quantities only. Give each form row a stable client ID; add another lot for a selected PO line and replace duplicate-PO-line restriction with grouped remaining validation. Preserve independent pricing/tax/date fields and allow shared product barcode. Autosave changes under captured owner and flush on leave/close; show save failures. Sending/unknown freezes editing, displays clear status plus “Check receipt” and “Retry same receipt”, and allows safe close retaining work. A definitive validation rejection re-enables fields with inline row errors; only matching confirmed success closes. A pending draft remains recoverable for a now-Received/Closed PO. Register/unregister scope hooks with component lifetime and guard async default responses by row identity/revision.
- [ ] **Extend receipt history and translations (RF5).** Keep rows keyed by `receiptLineId`, show both lots for one PO line and original quantity/cost/stock links; surface client identity in model/test assertions, not unnecessary operator copy. Keep batch navigation; no supplier-return actions. Add all operator messages in all existing locales and test key parity. History tests verify changed current batch quantity/retail terms do not replace original receipt figures.
- [ ] **Run green focused frontend tests and build.** `bun run test --run 'src/app/features/purchase-orders/**/*.spec.ts'`, `bun run test --run 'src/app/core/context/**/*.spec.ts'`, then `bun run build`. Expected: all pass, finalized protected service worker remains in the build, both entry points use the same receiving lifecycle.
- [ ] **Review and commit the durable UI.** Suggested commit: `feat: recover multi-lot purchase receiving drafts`.

### Task 3: Browser evidence, migration proof and complete verification

**Files:**

- Create `src/frontend/tests/ui-audit/release/purchase-receiving.spec.ts`.
- Create `src/frontend/tests/ui-audit/release/purchase-receiving-server.ts` if needed to serve the actual built Angular app plus deterministic API fixtures; reuse existing release-server conventions without changing existing cache-isolation behavior.
- Extend `tests/backend/integration/Intelibill.Integration.Tests/PurchaseOrdersControllerTests.cs` for RF5/migration evidence as needed.
- Update `docs/backend-architecture.md`, `docs/frontend-architecture.md`, and this plan's completed checkboxes/results.

**Interfaces:** Browser tests exercise the production-built dialog and pages. API fixtures may simulate response loss only for UI state evidence; xUnit/Testcontainers remain the proof of actual transaction/replay correctness. Do not label a fixture's counters as real database evidence.

- [ ] **Write RF5 integration evidence.** Receive A4/B6, sell 2 from A through the existing sales path, then apply an existing receipt-cost correction to A's `PurchaseReceiptValue`. Assert original receipt-line IDs, client IDs, quantities4/6, original costs, batch and stock-transaction links are unchanged; correction is additive and stock remains2/6. No supplier-return test/claim. Test old receipt rows backfill client ID from ID and null old-server writes still read with stable fallback; verify fresh schema and upgrade from the immediately preceding migration using disposable databases.
- [ ] **Write release UI scenarios (RF1–RF5).** Run against built Angular routes, not an isolated hand-written form: (1) detail and list validation rejection preserve entered barcode/batch/cost/tax/dates and editable rows; (2) 4+6 lots submit once and show two history rows; (3) accepted request with dropped response reloads into unknown, lookup confirms the original receipt, and repeat/retry never generates a fresh key; (4) lookup miss retains frozen payload and same-key retry; (5) unrelated success and scope switch cannot dismiss/clear the active draft; original owner can recover on return; (6) complete/closed PO still offers pending recovery; (7) storage rejection prevents request. Use targeted test IDs and deterministic fixtures with operation ledger, not arbitrary sleeps. Ensure changed PO responses do not overwrite existing form input.
- [ ] **Run release UI checks.** In `src/frontend`: `bun run build` then `bun x playwright test --config tests/ui-audit/release/playwright.release.config.ts purchase-receiving.spec.ts`, then `bun run audit:ui:release`. Expected: receiving scenarios and existing protected-cache-isolation scenarios pass. If the fixture server needs setup, document it in the spec and keep tests self-contained.
- [ ] **Run complete suites once implementation and review fixes settle.** Repo root: `dotnet build src/backend/Intelibill.slnx` and `dotnet test src/backend/Intelibill.slnx` (Docker running). In `src/frontend`: `bun run test --watch=false` and `bun run build` if changed since the last successful build. Run `git diff --check`. Record actual counts/results and material unverified limits; no success claim based on baseline counts.
- [ ] **Record rollout and rollback.** Document new type/slug, optional fields, grouped whole-unit rule, minimal metadata migration, row errors, owner-scoped unknown state and exact replay. Generate/inspect migration SQL with `dotnet ef migrations script <previous-migration> AddPurchaseReceiptClientLineIdentity --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api`. Migration job precedes new API; new API precedes UI. Retain replay-capable API and additive schema during rollback, hide adding lots/recovery UI only with a viable existing recovery path, and never delete pending drafts or operation records. Supplier returns and mobile UI remain outside scope.
- [ ] **Review and commit evidence/docs.** Suggested commit: `test: prove purchase receiving recovery and lineage`. Parent then performs independent branch review, fixes issues, reruns affected checks, and opens the authorized PR. Do not merge or deploy as part of this plan.

## Plan self-review

All five supplied review focuses have concrete tests above. October dependency assumptions were rechecked against owning sources. The plan retains legacy request/response compatibility and whole-unit PO semantics; new operation identity, stable row IDs, grouped validation, durable drafts, unknown recovery, scope safety, history and rollout are covered. Supplier-return integration was replaced with explicit future compatibility plus existing sale/cost-correction evidence because that subsystem is absent. No implementation blocker was found; migration and browser fixtures require actual verification during execution.

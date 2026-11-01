# Atomic Bulk Stock Intake Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every accepted bulk-intake row is a complete committed receipt; every rejected row leaves no business write; an interrupted request can be resent under the same operation key without posting any row twice.

**Architecture:** Keep the partial-success contract (`200`/`207`, `succeeded[]`/`failed[]`). Each row runs in its own DI scope (fresh `DbContext`, fresh item/inventory caches) and its own transaction. A keyed request first commits a manifest receipt (ordered row ids + row hashes) and then runs each row through plan 12's `IFinancialOperationExecutor` under a per-row receipt, so a resent request replays committed rows, re-evaluates rows that never committed, and answers `409 Operation.PayloadMismatch` if any row changed.

**Tech Stack:** ASP.NET Core 10, Wolverine, ErrorOr, FluentValidation, EF Core 10/Npgsql, xUnit + NSubstitute, Testcontainers; Angular 21, Vitest, Bun.

**Spec:** `pending-tasks/13-atomic-bulk-stock-intake-done.md` (pasted plan 13, P1); `docs/reviews/2026-10-21-market-readiness.md`; plan 12 (`docs/superpowers/plans/2026-11-01-idempotent-financial-commands.md`).

## Global Constraints

- Domain ← Application ← Infrastructure ← Api; repository abstractions, ErrorOr, `IUnitOfWork`; warnings-as-errors.
- Tenant and actor from authenticated context; authorize (membership + Owner/Manager) **before** reading or replaying any receipt.
- Never catch a PostgreSQL error and keep using its transaction or context: each row's scope is disposed after the row.
- Never log an exception message; log the exception type name only.
- Web only (mobile excluded); Bun only.

## Gap analysis (plan 13 → `main` at `7fe40d43`)

| Requirement | State on `main` | This plan |
|---|---|---|
| Row isolation | One tracker for all rows; a row failing after `ItemResolver` added a new item still saves that item with the next successful row | Task 1: fresh scope + transaction per row |
| Duplicate item+batch in one request | Both rows staged; the unique index fails the whole `SaveChanges` (500, nothing saved) | Task 1: rows commit in order, the later row answers `Inventory.BatchNumberAlreadyExists` |
| Zero-cost receipt | Batch + movement tracked, then `SupplierLedgerEntry.AmountZero` fails the row | Task 1: stock without a payable; void and supplier reassignment tolerate the missing payable |
| Resume after response loss | Resending reposts every row | Task 2: manifest + per-row receipts |
| Changed payload under same operation | n/a | Task 2: `409 Operation.PayloadMismatch` before any row runs |
| Duplicate `ClientRowId` | Accepted; response ambiguous | Task 2: `400 Inventory.DuplicateClientRowId` before any row runs |
| Transient storage failure | 500 for the whole request (or silent loss of tracked rows) | Task 2: row reported `retryable: true` with `Inventory.RowRetryable`; other rows continue |
| Cancellation | Partial summary impossible to distinguish | Task 2: cancellation propagates (no summary); resend under the same key recovers |
| Web resend reuses the same payload | `performedAt` regenerated per send; no key | Task 3: frozen submission in IndexedDB |

### Deviation from the pasted plan

The pasted plan proposed new `BulkIntakeOperation`/`BulkIntakeRowResult` tables. Plan 12 already shipped `operation_receipts` (RLS-isolated, unique per shop + type + key, reserve-run-commit in one transaction, replay or `PayloadMismatch`) and its plan recorded that "plan 13 keeps per-row receipts … adopt `IFinancialOperationExecutor`". Reusing it gives the same manifest/row-result semantics with no new table, migration or RLS policy, and inherits the proven concurrency algorithm. Rejected rows are **not** persisted as results: they have no side effects by definition, so a resend re-evaluates them (and a row whose cause was fixed, e.g. a supplier created meanwhile, can then succeed).

## Contracts

- `FinancialOperationType` gains `BulkIntakeManifest = 3`, `BulkIntakeRow = 4` (no route slug: the recovery path is resending the same request).
- `AddInventoryBatchRequest`/`AddInventoryBatchCommand` gain optional `operationKey` (1–60 chars after trim). Absent → legacy unkeyed path (still row-isolated) with a `Legacy unkeyed financial command` warning.
- `ClientRowId`: required, 1–64 chars, unique (ordinal) within the request — `Inventory.ClientRowIdInvalid` / `Inventory.DuplicateClientRowId` (400) before authorization or any write.
- Row receipt key: `{operationKey.Length}:{operationKey}:{clientRowId}` (length prefix keeps it unambiguous; ≤ 128).
- Manifest hash: `FinancialOperationHasher.Compute(ordered [(clientRowId, rowHash)])`; row hash covers every business field (amounts via `NormalizeAmount`, optional strings trimmed/nullified).
- `IInboundRowExecutor.ExecuteAsync(shopId, actorUserId, InboundRowOperation? operation, InboundInventoryLineInput row, ct) : Task<ErrorOr<AddInventoryResultDto>>` — Application port; Infrastructure implementation creates an async scope per row.
- `Errors.Inventory.RowRetryable` (`Failure`); failed-row DTO gains `retryable: bool`.
- `BatchCreationResult.LedgerEntry` and `InboundInventoryLineResult.LedgerEntry` become nullable: `null` exactly when the rounded receipt value is zero.

## Review Focus

1. Late supplier failure after a new item is staged, in both row orders → only the valid row's item/batch/movement/inventory/liability exist (`ValidThenInvalidSupplier_LeavesNoFailedItem`, `InvalidSupplierThenValid_LeavesNoFailedItem`).
2. Zero-cost receipt → quantity 5, one `IN(+5)`, no supplier ledger entry, 200 (`ZeroCostReceipt_HasStockWithoutPayable`); such a batch can still be voided.
3. Duplicate item+batch rows → one receipt, later row 409-coded failure (`DuplicateBatchWithinRequest_OnlyOneReceipt`).
4. Response lost after commit → resend replays the same row results, no extra quantity/liability (`ResponseLoss_ResumesOnlyUnfinishedRows`); changed row → 409 with nothing changed (`ChangedRowOnReplay_Conflicts`).
5. Storage fault in one row → that row `retryable`, nothing persisted for it, later rows committed; resend commits it once (`TransientFailure_RemainsRetryable`).

---

### Task 1: Isolated row outcomes and zero-cost receipts

**Files:** create `Application/Features/Inventory/Services/IInboundRowExecutor.cs`, `Infrastructure/Services/Inventory/InboundRowExecutor.cs`, `tests/.../Features/Inventory/BulkIntakeAtomicityTests.cs`; modify `AddInventoryBatchCommand(Handler)`, `BatchFactory`, `IBatchFactory`, `InboundInventoryLineInput`, `AddInventoryCommandHandler`, `VoidBatchCommandHandler`, `ReassignBatchSupplierCommandHandler`, `Errors.Inventory`, Infrastructure `DependencyInjection`.

- [ ] Write `ValidThenInvalidSupplier_LeavesNoFailedItem` (+ reversed order) and `ZeroCostReceipt_HasStockWithoutPayable`; run `BulkIntakeAtomicityTests` → red.
- [ ] Row executor: scope per row → processor with fresh contexts → `SaveChanges` → `AddInventoryResultDto`; any exception except cancellation → `Inventory.RowRetryable` (type name logged).
- [ ] Handler: validate → authorize → per row `IInboundRowExecutor`; no handler-level `SaveChanges`.
- [ ] Zero cost: skip the payable when the rounded value is 0 (bulk and single inbound); void skips a zero reversal; supplier reassignment of a payable-less batch reassigns the batch only.
- [ ] Unit: handler tests rewritten over `IInboundRowExecutor`; `BatchFactory` zero-cost test; void/reassign zero-cost tests.

### Task 2: Durable manifest, resume and retryable rows

**Files:** modify `FinancialOperationType`, handler, controller + requests, `Errors.Inventory`; extend `BulkIntakeAtomicityTests`, handler unit tests.

- [ ] Write `DuplicateBatchWithinRequest_OnlyOneReceipt`, `ResponseLoss_ResumesOnlyUnfinishedRows`, `ChangedRowOnReplay_Conflicts`, `DuplicateClientRowId_RejectsBeforeProcessing`, `TransientFailure_RemainsRetryable` (test-only `SaveChangesInterceptor` that throws for one barcode).
- [ ] Keyed path: manifest receipt via `IFinancialOperationExecutor` (no-op action) → per-row receipts; order of output preserved; 100-row limit kept.
- [ ] Cancellation: `ThrowIfCancellationRequested` between rows; no catch of cancellation.
- [ ] Run `BulkIntakeAtomicityTests`, `AddInventoryBatchTests`, `InventoryControllerTests`, `FinancialOperationTests` and `AddInventoryBatchCommandHandlerTests`.

### Task 3: Web frozen submissions

**Files:** modify `inventory-draft-indexeddb.service.ts` (v2 store `inventory-inbound-submission`), `batch-draft-state.service.ts`, `inventory-batch-page.component.ts/.html`, `inventory.models.ts`, nine i18n files; specs.

- [ ] Save freezes the rows (including `performedAt`) into chunks of ≤100 with keys `{submissionId}-{index}`, persists before sending (storage failure ⇒ nothing sent).
- [ ] Unknown outcome (status 0/408/429/5xx/non-HTTP) keeps the frozen submission and shows `inventory.saveInterrupted`; the next Save resends it unchanged. Definitive responses clear it.
- [ ] Rows kept = current pending rows not reported succeeded.
- [ ] Specs: resend after network failure reuses the frozen key and payload; a definitive response clears the submission.

## Verification

`dotnet build src/backend/Intelibill.slnx`; unit test projects; `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter "FullyQualifiedName~BulkIntakeAtomicityTests|FullyQualifiedName~AddInventoryBatchTests|FullyQualifiedName~InventoryControllerTests|FullyQualifiedName~FinancialOperationTests|FullyQualifiedName~TenantPolicyManifest"`; `bun run test`, `bun run build` in `src/frontend`.

## Rollout / rollback

No schema change. Server accepts optional keys; web sends them. Rollback keeps committed receipts (they are never deleted). Existing partial rows from the old shared-tracker path cannot be repaired from logs: reconciliation of batch quantity vs. signed movements vs. inventory is plan 14's read-only report with owner-approved corrections; nothing is deleted automatically.

## Deferred

- Requiring `operationKey` once `Legacy unkeyed financial command` warnings for bulk intake stop.
- Purchase-order receipts (plan 30/31) and single inbound keys.
- Domain events from `SaveChanges` publish before the executor commits (pre-existing in plan 12's path); durable delivery is plan 47.

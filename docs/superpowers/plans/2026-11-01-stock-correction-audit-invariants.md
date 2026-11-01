# Stock correction audit invariants — implementation plan (plan 14)

**Goal:** every quantity-changing success has exactly one matching signed movement, written in the
same commit as the batch and aggregate change, and attributable to an actor and a reason.

**Invariant (per item):** `sum(signed physical movements) = inventory.quantity = sum(active batch quantities)`.

## Decisions taken while implementing

| Question | Decision |
|---|---|
| Where does a count live? | New `POST inventory/batches/{batchId}/count` (`CorrectStockCountCommand`). Every count writes a `stock_counts` row (observed, counted, delta, reason, actor). A nonzero delta also writes one `InventoryAdjustment` (reason `StockCountCorrection`) and one signed `StockTransaction` (`In +d` / `Out -d`). A zero delta writes the count row only. |
| Version for `ExpectedVersion` | PostgreSQL `xmin` mapped as a row-version concurrency token on `inventory_batches` **and** `inventory`. No column is added; every writer of either row now gets an optimistic-concurrency check, so a stale write raises a conflict instead of silently overwriting. |
| Conflict answer | A stale `expectedVersion` or a lost race answers 409 `Inventory.BatchVersionConflict`. Any other `DbUpdateConcurrencyException` reaching the API answers 409 `Concurrency.Conflict` instead of 500. The client never reapplies a stale count. |
| Replay | Optional `operationKey` runs through `IFinancialOperationExecutor` (`FinancialOperationType.StockCount = 5`). |
| Void of a consumed receipt | Refused with 409 `Inventory.BatchConsumed` when the batch quantity differs from its receipt quantity, it has any non-receipt movement, or it has a live adjustment. Remaining stock is removed with a count (or supplier return, plan 31); the original payable is untouched. |
| Unconsumed void | One `Reversal(-originalQuantity)` movement (= remaining), aggregate reduced by the same amount, payable reversed; single save, no retry loop that could reload and drop the void. |
| Legacy `PUT batches/{id}` | Kept (price/metadata edits depend on it until plan 15). Fixed: signed delta (`Out -d` for a decrease), every movement/ledger `ErrorOr` failure aborts before save. |
| Zero-cost adjustments | `cost_impact` relaxed to `>= 0` so free stock and sub-paisa deltas can be counted. |
| Quantity precision | Adjustment quantity and snapshots move to `numeric(18,3)` like stock, so 3-place batches (kg) can be counted. |
| Stale legacy edit | `PUT batches/{id}` takes optional `expectedVersion` (the web always sends it); a stale form answers 409 instead of undoing a sale. |
| Supplier ledger | `ck_supplier_ledger_entries_batch_by_type` admits `REVERSAL`; before, voiding any priced receipt failed with 500. |

## Tasks

1. Integration fixture `StockCorrectionInvariantTests` (red first): 10→8 writes `Out(-2)`; 8→10 writes `In(+2)`;
   equal count writes no movement; late audit failure rolls everything back; partly sold void refused and
   count-to-zero removes 6 with payable unchanged; two counts on one version → one commits, one 409;
   stale version; staff refused; reason required; keyed replay. Each asserts the reconciliation invariant.
2. Domain + persistence: `StockCount` entity, `Version` tokens, `FinancialOperationType.StockCount`,
   migration (`stock_counts` + RLS + relaxed check), repository, tenant manifest.
3. Application: `CorrectStockCount` command/validator/handler; `VoidBatch` consumption guard; `UpdateInventoryBatch` sign/error fixes.
4. Api: endpoint, request contract, `DbUpdateConcurrencyException` → 409.
5. Web: batch `version`, "Count stock" dialog on the batches page (reason required, shows movement/adjustment
   reference, 409 → reload with guidance and no auto-reapply), consumed-void guidance, i18n in all locales.
6. Unit tests for the handlers; run unit + integration + frontend suites; docs.

## Out of scope / user decisions

- Who may approve stock losses (currently owner/manager, same as adjustments).
- Whether zero-delta confirmations appear in a separate stocktake report (stored now, not yet reported).
- Feature-gating the legacy batch `PUT` (plan 15).
- Discrepancy report for historical batches whose movements do not reconcile (read-only, separate task).

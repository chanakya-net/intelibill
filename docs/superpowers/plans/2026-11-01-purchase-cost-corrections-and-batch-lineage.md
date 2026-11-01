# Purchase cost corrections and batch lineage — implementation plan (plan 15)

**Goal:** separate retail pricing, physical stock, receipt liability and valuation corrections so a
price edit never replaces the lot, a cost correction posts only its delta, and historical sale
snapshots and returns stay intact.

**Source:** `pending-tasks/15-purchase-cost-corrections-and-batch-lineage.md`.

## What is wrong today

- `PUT inventory/batches/{id}` voids the batch and creates a replacement, reverses the full original
  payable and posts `remaining quantity × new cost`. Buy 10 @ ₹100, sell 4, set cost ₹110 → ₹660
  payable instead of ₹1000/₹1100. Returns of the old sale then hit `Sale.ReturnBatchVoided`.
- Nothing stores the authoritative receipt total for direct inbound: the batch keeps a rounded unit
  cost only. Void reverses `round(originalQuantity × unit cost)`, which can differ from the posted
  payable by the rounding remainder.
- Intake web form copies the sales `taxIncluded` into `purchaseTaxIncluded`
  (`batch-row-form-state.service.ts`), so purchase tax mode can never be set on its own.

## Decisions

| Question | Decision |
|---|---|
| Physical identity | `InventoryBatch.Id` is the lot for its whole life. No flow replaces a batch any more. |
| Receipt fact | New `PurchaseReceiptValue` (`purchase_receipt_values`), one per batch, written by `BatchFactory` for every receipt (direct, bulk, PO). Holds received quantity, **original total cost** (the rounded total the payable was posted with), **effective total cost**, supplier, source (`DirectInbound` / `PurchaseOrder` / `LegacyBackfill`), PO receipt-line link, status (`Resolved` / `LegacyUnresolved`), `xmin` version. Original is immutable; effective changes only through a cost correction. |
| Cost correction | `POST inventory/receipts/{receiptValueId}/cost-corrections` → `CorrectReceiptCostCommand(ReceiptValueId, RevisedTotalCost, Reason, ExpectedVersion, OperationKey?)`. `delta = revised − effective`; writes one `PurchaseReceiptCostAdjustment` (prior, revised, delta, reason, actor, ledger link) and one supplier ledger entry of new type `CostAdjusted` (signed delta, batch-linked). Owner only. Keyed via `IFinancialOperationExecutor` (`FinancialOperationType.ReceiptCostCorrection = 6`). |
| Valuation | Cost correction does **not** change `InventoryBatch.CostPrice` or any sale COGS snapshot. `RevalueRemainingStock` is deferred until the accounting treatment of the delta (inventory vs period variance) is approved. |
| Legacy evidence | Migration backfills receipt values for active batches. Resolved only when the batch has an opening `In` movement for its full original quantity that is not a legacy `Batch Correction` and at most one `GoodsReceived` entry; amount from the PO receipt line when present, else the posted payable, else zero for a free receipt. Everything else is `LegacyUnresolved` with **null** amounts — never estimated from remaining stock — and cost correction answers 422 `PurchaseReceipt.EvidenceMissing`. |
| Price edit | `PUT inventory/batches/{id}/prices` → `UpdateBatchPricesCommand` (MRP, sales price, tax rate, tax included, plus non-financial lot details: batch number, expiry, manufacturing date; optional reason; **required** `expectedVersion`). Updates the batch in place, appends a `BatchPriceRevision` (old/new MRP, sale price, tax rate, tax mode) when a price field changed, publishes `InventoryBatchPricingChangedDomainEvent`. Stock and payable untouched. Owner or manager. |
| Version guard | Price edits always write the batch row against `ExpectedVersion`; a stale form answers 409 `Inventory.BatchVersionConflict` even when quantity is unchanged. |
| Legacy `PUT batches/{id}` | Kept for compatibility, no longer destructive: a quantity change answers 400 `Inventory.BatchQuantityEditMoved` (use count), a cost change `Inventory.BatchCostEditMoved` (use receipt cost correction), a supplier change `Inventory.BatchSupplierEditMoved` (use reassign); otherwise applied in place exactly like the price endpoint. |
| Void | Reverses the receipt's **effective** total (original + adjustments) when the receipt is resolved, so a corrected-then-voided unconsumed receipt nets to zero. |
| Supplier reassignment | Moves the goods-received entry, every cost-adjustment entry and the receipt value together. |
| Returns | Unchanged validator; the original lot is never voided by a price/cost edit, so returns restock it. Batches already replaced by the old PUT stay voided; choosing a destination lot for those returns is a user policy decision (not automated here). |

## API contract (web depends on this)

- `InventoryBatchDto` gains `receiptValueId: string | null`, `receiptStatus: 'Resolved' | 'LegacyUnresolved' | null`, `receiptOriginalTotalCost: number | null`, `receiptTotalCost: number | null` (effective), `receiptVersion: number | null`.
- `PUT /api/inventory/batches/{batchId}/prices` body `{ expectedVersion, mrp, salesPrice, taxRatePercent, taxIncluded, batchNumber?, expiryDate?, manufacturingDate?, reason? }` → 200 `{ batchId, version, priceRevisionId | null }`. 409 `Inventory.BatchVersionConflict`.
- `POST /api/inventory/receipts/{receiptValueId}/cost-corrections` body `{ expectedVersion, revisedTotalCost, reason, operationKey? }` → 200 `{ adjustmentId, receiptValueId, batchId, priorTotalCost, revisedTotalCost, delta, supplierLedgerEntryId, receiptVersion, performedAt }`. Errors: 409 `PurchaseReceipt.VersionConflict`, 422 `PurchaseReceipt.EvidenceMissing`, 400 `PurchaseReceipt.CostUnchanged`, 400 `PurchaseReceipt.ReasonRequired`, 403 owner only.

## Tasks

1. **Red fixtures** — `PurchaseCostLineageTests`: price-only edit keeps batch/qty/payable/sale snapshot;
   return after price edit restocks original lot; two price edits on one version → one 409;
   partly-sold 10×₹100 → ₹1100 posts +₹100 only; successive price-then-cost edits no duplicate
   liability; legacy unresolved receipt blocks correction; legacy PUT quantity change refused;
   keyed replay; void after correction nets payable to zero; manager refused for cost correction.
2. **Domain** — `PurchaseReceiptValue`, `PurchaseReceiptCostAdjustment`, `BatchPriceRevision`,
   enums, `SupplierLedgerEntryType.CostAdjusted`, `FinancialOperationType.ReceiptCostCorrection`,
   `InventoryBatch.RevisePrices`/`ReviseDetails`; domain unit tests.
3. **Persistence** — configurations, repositories, DbSets, tenant manifest, migration with backfill,
   RLS and ledger check update.
4. **Application** — `BatchFactory` writes receipt values; PO receive links lines; `UpdateBatchPrices`,
   `CorrectReceiptCost`; legacy PUT rewritten; void and reassign use receipt value; batch DTO.
5. **Api** — two endpoints and request contracts.
6. **Web** — independent purchase-tax control (old drafts without it must be confirmed); batches page:
   "Edit prices" dialog on the new endpoint, "Correct receipt cost" dialog (owner, shows
   original/current total and the delta, unresolved → guidance), i18n in every locale.
7. **Verify** — unit, integration, frontend suites; docs (`backend-architecture.md`, CLAUDE.md snapshot).

## Out of scope / user decisions

- Accounting treatment of a cost delta (inventory revaluation vs current-period variance) and the
  `RevalueRemainingStock` action.
- Destination policy for returns of sales from batches the old PUT already replaced.
- Tax adjustment documents for cost corrections (plan 20).

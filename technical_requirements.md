# Inventory Adjustment Technical Requirements

## Summary

Build full backend and frontend support for inventory adjustments. A shop user must be able to increase or decrease current inventory on an existing batch for reasons such as missing, damaged, expired, stolen, wastage, found stock, or count correction. Adjustments must update batch quantity, aggregate item inventory, stock transaction history, profit/loss reporting, dashboard financial KPIs, and provide an auditable adjustment history.

## Resolved Decisions

- Adjustments are batch-specific. Item-level adjustment without a batch is not allowed.
- Adjustments support both increase and decrease.
- Quantity is always positive in the API and UI; direction carries the sign.
- Adjustments target existing non-voided batches only. They do not create new batches.
- Decrease adjustments cannot reduce a batch below zero.
- Adjustment quantity supports up to 2 decimal places.
- Users may backdate `performedAt`; future dates are rejected.
- Notes are optional except for `OtherLoss` and `OtherGain`.
- Attachments are out of scope for v1.
- Bulk adjustments are out of scope for v1.
- CSV export is out of scope for v1.
- Adjustment history is included in v1 and is server-side paginated.
- Adjustment losses appear in profit/loss by default.
- Dashboard financial KPIs and trends include active decrease adjustment losses.
- Increase adjustments do not create profit/loss impact.
- Adjustments do not affect supplier ledger or expense ledger in v1.
- Adjustment void/reversal is in scope.
- Voiding marks the original adjustment as voided and creates a reversal `StockTransaction`; it does not create a second adjustment row.

## Permissions

- `GET /api/inventory/adjustments`: Owner, Manager, Staff.
- `POST /api/inventory/batches/{batchId}/adjust`: Owner, Manager.
- `POST /api/inventory/adjustments/{adjustmentId}/void`: Owner only.

Handlers must still validate shop membership and active shop ownership server-side, not rely only on endpoint policies.

## Domain Model

### New Enums

```csharp
public enum InventoryAdjustmentDirection
{
    Increase = 1,
    Decrease = 2
}

public enum InventoryAdjustmentReason
{
    Damaged = 1,
    Expired = 2,
    Stolen = 3,
    MissingLost = 4,
    StockCountCorrection = 5,
    OtherLoss = 6,
    FoundStock = 7,
    ReturnRestockCorrection = 8,
    OtherGain = 9
}
```

Reason must be compatible with direction:

- Decrease: `Damaged`, `Expired`, `Stolen`, `MissingLost`, `StockCountCorrection`, `OtherLoss`.
- Increase: `FoundStock`, `StockCountCorrection`, `ReturnRestockCorrection`, `OtherGain`.

### New Entity: `InventoryAdjustment`

Fields:

- `Id`
- `ShopId`
- `ItemId`
- `InventoryBatchId`
- `StockTransactionId`
- `ReversalStockTransactionId`
- `AdjustmentNumber`
- `Direction`
- `Reason`
- `Quantity`
- `UnitCost`
- `CostImpact`
- `BatchQuantityBefore`
- `BatchQuantityAfter`
- `InventoryQuantityBefore`
- `InventoryQuantityAfter`
- `Notes`
- `PerformedAt`
- `PerformedBy`
- `CreatedBy`
- `IsVoided`
- `VoidedAt`
- `VoidedBy`
- `VoidReason`

Validation:

- `AdjustmentNumber` required.
- `Quantity > 0`.
- `Quantity` max 2 decimal places.
- `UnitCost >= 0`.
- `CostImpact > 0`.
- `OtherLoss` and `OtherGain` require notes.
- `VoidReason` required when voiding.
- Cannot void twice.

Cost impact:

- Decrease: `CostImpact = Quantity * InventoryBatch.CostPrice`.
- Increase: `CostImpact = Quantity * InventoryBatch.CostPrice`.

Financial reporting only treats active decrease adjustments as losses. Increase adjustments store the operational cost basis for audit/history but do not create revenue or profit/loss rows.

## Data Layer

Add:

- `DbSet<InventoryAdjustment> InventoryAdjustments`.
- `IInventoryAdjustmentRepository`.
- `InventoryAdjustmentRepository`.
- EF configuration.
- EF migration.

Recommended table: `inventory_adjustments`.

Indexes:

- Unique: `(shop_id, adjustment_number)`.
- `(shop_id, performed_at)`.
- `(shop_id, inventory_batch_id, performed_at)`.
- `(shop_id, item_id, performed_at)`.
- `(shop_id, reason, performed_at)`.
- `(shop_id, direction, performed_at)`.

Relationships:

- `Shop` cascade delete.
- `Item` restricted/composite relation with shop isolation.
- `InventoryBatch` restricted/composite relation with item and shop isolation.
- `StockTransactionId` restricted.
- `ReversalStockTransactionId` restricted, nullable.
- `PerformedBy`, `CreatedBy`, `VoidedBy` reference users where current project conventions allow.

Store enums as strings with stable provider values. Suggested values:

- Direction: `Increase`, `Decrease`.
- Reasons: `Damaged`, `Expired`, `Stolen`, `MissingLost`, `StockCountCorrection`, `OtherLoss`, `FoundStock`, `ReturnRestockCorrection`, `OtherGain`.

## Number Generation

Add an application service:

```csharp
public interface IInventoryAdjustmentNumberGenerator
{
    string Generate(DateTimeOffset? now = null);
}
```

Format:

```text
ADJ-yyyyMMdd-XXXXXXXX
```

Example:

```text
ADJ-20260505-AB12CD34
```

The backend generates this only. It is unique per shop and used as `StockTransaction.ReferenceNumber`.

## Stock Transactions

Create one `StockTransaction` for every adjustment.

Quantity sign:

- Increase: positive.
- Decrease: negative.

Transaction type mapping:

- `Damaged` -> `Dmg`.
- `Stolen` -> `Stol`.
- Other decrease reasons -> `Adj`.
- Increase reasons -> `Adj`.

Void creates a reversal stock transaction:

- Original decrease void: positive reversal.
- Original increase void: negative reversal.
- Reference can be `VOID-{AdjustmentNumber}`.
- Original adjustment stores `ReversalStockTransactionId`.

Note: current stock transaction sign validation should be reviewed because `Reversal` provider mapping exists, but the check constraint shown in `StockTransactionConfiguration` does not include `REV`. The implementation must ensure reversal transactions are valid at both domain and database levels.

## Backend API

### Create Adjustment

```http
POST /api/inventory/batches/{batchId:guid}/adjust
```

Policy: `OwnerOrManager`.

Request:

```csharp
public sealed record AdjustInventoryRequest(
    string Direction,
    string Reason,
    decimal Quantity,
    DateTimeOffset? PerformedAt,
    string? Notes);
```

Response:

```csharp
public sealed record AdjustInventoryResultDto(
    Guid AdjustmentId,
    string AdjustmentNumber,
    Guid BatchId,
    Guid ItemId,
    string Direction,
    string Reason,
    decimal Quantity,
    decimal UnitCost,
    decimal CostImpact,
    decimal BatchQuantityBefore,
    decimal BatchQuantityAfter,
    decimal InventoryQuantityBefore,
    decimal InventoryQuantityAfter,
    Guid StockTransactionId,
    DateTimeOffset PerformedAt);
```

Behavior:

- Validate auth and active shop.
- Validate batch exists, belongs to active shop, and is not voided.
- Validate direction, reason compatibility, notes, quantity precision, and timestamp.
- For decrease, validate `quantity <= batch.Quantity`.
- Load aggregate inventory for `(shopId, itemId)`.
- Capture before quantities.
- Mutate `InventoryBatch.Quantity`.
- Mutate aggregate `Inventory.Quantity`.
- Create stock transaction.
- Create inventory adjustment.
- Save atomically.
- Return before/after result.

Concurrency:

- The full operation must be transactional.
- If concurrent stock changes occur, use EF concurrency/retry patterns consistent with existing inventory mutation code.
- A failed concurrent update should return a conflict with a clear inventory update conflict code.

### Adjustment History

```http
GET /api/inventory/adjustments
```

Policy: authenticated shop member. Owner, Manager, Staff may view.

Query params:

```text
pageNumber=1
pageSize=25
itemId=
batchId=
direction=
reason=
from=
to=
includeVoided=true
```

Rules:

- Default `pageNumber = 1`.
- Default `pageSize = 25`.
- Max `pageSize = 100`.
- Default `includeVoided = true`.
- Sort by `performedAt desc`, then `createdAt desc`.
- Filters apply before paging.

DTO:

```csharp
public sealed record InventoryAdjustmentDto(
    Guid Id,
    string AdjustmentNumber,
    Guid ItemId,
    string ItemName,
    string Barcode,
    Guid BatchId,
    string BatchNumber,
    string Direction,
    string Reason,
    decimal Quantity,
    decimal UnitCost,
    decimal CostImpact,
    decimal BatchQuantityBefore,
    decimal BatchQuantityAfter,
    decimal InventoryQuantityBefore,
    decimal InventoryQuantityAfter,
    string? Notes,
    DateTimeOffset PerformedAt,
    Guid PerformedByUserId,
    string PerformedByName,
    bool IsVoided,
    DateTimeOffset? VoidedAt,
    Guid? VoidedByUserId,
    string? VoidedByName,
    string? VoidReason);
```

Name fallback:

- Prefer user display/name field.
- Fallback to email.
- Fallback to phone.
- Fallback to user id string.

Return a paginated DTO consistent with existing app style, or introduce a shared paginated response if needed.

### Void Adjustment

```http
POST /api/inventory/adjustments/{adjustmentId:guid}/void
```

Policy: `OwnerOnly`.

Request:

```csharp
public sealed record VoidInventoryAdjustmentRequest(string Reason);
```

Behavior:

- Validate auth and active shop.
- Validate adjustment exists in active shop.
- Validate not already voided.
- Validate void reason.
- Validate reversal will not create negative stock.
  - Voiding an original increase subtracts quantity; if that stock has since been sold/adjusted away, return conflict.
  - Voiding an original decrease adds quantity and is allowed for non-voided batch.
- Mutate batch and aggregate inventory.
- Create reversal stock transaction.
- Mark original adjustment voided with metadata and `ReversalStockTransactionId`.
- Save atomically.
- Profit/loss excludes voided adjustments entirely.

## Profit/Loss Report

Existing `/api/sales/profit-loss` must include active decrease adjustment losses by default.

Refactor DTO names to support non-sale rows:

```csharp
public enum ProfitLossRowType
{
    Sale = 1,
    SaleReturn = 2,
    InventoryAdjustment = 3
}

public sealed record ProfitLossReportItemDto(
    Guid? SaleId,
    string ReferenceNumber,
    DateTimeOffset OccurredAt,
    string? PartyName,
    decimal TotalCost,
    decimal WastageCost,
    decimal RevenueBeforeTax,
    decimal RevenueAfterTax,
    decimal ProfitBeforeTax,
    decimal ProfitAfterTax,
    ProfitLossRowType RowType,
    Guid? InventoryAdjustmentId);
```

Adjustment row values:

- `SaleId = null`.
- `ReferenceNumber = AdjustmentNumber`.
- `OccurredAt = PerformedAt`.
- `PartyName = "Inventory adjustment"` or localized frontend label.
- `TotalCost = 0`.
- `WastageCost = CostImpact`.
- `RevenueBeforeTax = 0`.
- `RevenueAfterTax = 0`.
- `ProfitBeforeTax = -CostImpact`.
- `ProfitAfterTax = -CostImpact`.
- `RowType = InventoryAdjustment`.
- `InventoryAdjustmentId = adjustment.Id`.

Sort all rows by `OccurredAt desc`.

## Dashboard

Dashboard backend must include active decrease adjustment losses within the selected date range.

Affected places:

- `WastageCost`: add decrease adjustment `CostImpact`.
- `ProfitBeforeTax`: subtract decrease adjustment `CostImpact`.
- `ProfitAfterTax`: subtract decrease adjustment `CostImpact`.
- `ProfitTrendSeries`: subtract daily decrease adjustment impacts on the matching `performedAt` date.
- `PreviousPeriodSummary.ProfitAfterTax`: subtract previous period decrease adjustment impacts.
- `HasNoSalesActivity`: false when there are sales, active sale returns, or active decrease adjustments.

Increase adjustments do not affect financial KPIs.

Staff behavior:

- Staff still sees stock effects through inventory quantities and alerts.
- Financial KPIs remain hidden where they are currently hidden.

## Frontend Scope

Implementation is backend plus full frontend. Nothing in the user workflow should be left API-only.

### Inventory Batches Page

Add per-row Adjust action on desktop and mobile for non-voided batches.

Dialog:

- Direction segmented control: `Increase`, `Decrease`.
- Reason dropdown filtered by direction.
- Quantity numeric input with max 2 fraction digits and positive-only validation.
- Optional `performedAt` input defaulting to now.
- Notes textarea/input.
- Notes required for `OtherLoss` and `OtherGain`.
- Before/after preview:
  - Current batch quantity.
  - New batch quantity.
  - Unit cost.
  - Loss/P&L impact.
- Disable submit while saving.
- On success, show confirmation and refresh batch list.

Preview:

- Decrease: `newBatchQty = currentQty - quantity`, P/L impact = `quantity * costPrice`.
- Increase: `newBatchQty = currentQty + quantity`, P/L impact = `0`.

### Adjustment History Page

Add route:

```text
/inventory/adjustments
```

Navigation:

- Add shell/menu entry under inventory.
- Link from batch page to adjustment history.

Page features:

- Paginated table/list.
- Mobile-friendly card layout.
- Filters:
  - item/batch search or selector.
  - direction.
  - reason.
  - date from/to.
  - include voided toggle.
- Default page size 25.
- New Adjustment button.
- New Adjustment flow includes searchable batch selector.
- Batch selector shows all non-voided batches.
  - For decrease, zero-quantity batches are disabled or clearly unavailable.
  - For increase, zero-quantity batches are selectable.
- Owner-only void action for non-voided adjustments.
- Void dialog requires reason.
- Show performed by display name.
- Show status tag: Active/Voided.
- Show void reason and voided metadata when applicable.

### Profit/Loss Frontend

Update frontend service/model and table for renamed DTO fields:

- `referenceNumber`
- `occurredAt`
- `partyName`
- `rowType`
- `inventoryAdjustmentId`

Display adjustment rows clearly:

- Use row type label such as `Inventory adjustment`.
- Show wastage/loss amount.
- No customer/sale link for adjustment rows unless later added.

### Dashboard Frontend

No major UX change is required if backend preserves existing DTO shape, but tests and labels must reflect that `wastageCost` can include adjustment losses.

If helpful, tooltip/copy may say wastage includes returns and inventory adjustments.

### Localization

Add all new keys for supported locales, including:

- Adjustment actions and page labels.
- Direction labels.
- Reason labels.
- Validation errors.
- Void labels.
- History filters.
- Profit/loss row type label.
- Dashboard wastage tooltip/copy if added.

## Error Codes

Recommended new errors:

- `InventoryAdjustment.BatchNotFound`
- `InventoryAdjustment.BatchVoided`
- `InventoryAdjustment.DirectionInvalid`
- `InventoryAdjustment.ReasonInvalid`
- `InventoryAdjustment.ReasonDirectionMismatch`
- `InventoryAdjustment.QuantityMustBePositive`
- `InventoryAdjustment.QuantityPrecisionInvalid`
- `InventoryAdjustment.InsufficientStock`
- `InventoryAdjustment.FutureDateNotAllowed`
- `InventoryAdjustment.NotesRequired`
- `InventoryAdjustment.NotFound`
- `InventoryAdjustment.AlreadyVoided`
- `InventoryAdjustment.VoidReasonRequired`
- `InventoryAdjustment.VoidWouldCreateNegativeStock`
- `InventoryAdjustment.UserIsNotOwnerOrManager`
- `InventoryAdjustment.UserIsNotOwner`
- `InventoryAdjustment.UpdateConflict`

## Testing Requirements

### Domain Unit Tests

- Create decrease adjustment succeeds.
- Create increase adjustment succeeds.
- Reject zero/negative quantity.
- Reject more than 2 decimal places.
- Reject reason/direction mismatch.
- Require notes for `OtherLoss` and `OtherGain`.
- Void requires reason.
- Cannot void twice.

### Application Unit Tests

Create adjustment command:

- Owner can adjust.
- Manager can adjust.
- Staff cannot adjust.
- Missing user/shop/membership errors.
- Batch not found or wrong shop rejected.
- Voided batch rejected.
- Decrease over available quantity rejected.
- Increase updates batch and inventory.
- Decrease updates batch and inventory.
- Correct stock transaction type and signed quantity created.
- Adjustment records before/after quantities.
- Future `performedAt` rejected.

Void adjustment command:

- Owner can void.
- Manager/Staff cannot void.
- Already voided rejected.
- Reversal stock transaction created.
- Batch and inventory restored for original decrease.
- Conflict if voiding original increase would create negative stock.

History query:

- Staff can view.
- Filters by item, batch, direction, reason, date range, include voided.
- Pagination and sorting.
- Performed by fallback display name.

Profit/loss query:

- Includes active decrease adjustments as loss rows.
- Excludes increase adjustments.
- Excludes voided adjustments.
- Uses new DTO field names and row types.

Dashboard query/calculator:

- Wastage includes decrease adjustments.
- Profit before/after tax subtract decrease adjustments.
- Profit trend subtracts daily adjustment losses.
- Previous period summary includes adjustment losses.
- `HasNoSalesActivity` false when only decrease adjustments exist.
- Staff financial hiding remains intact.

### Integration Tests

- Create decrease adjustment through API updates:
  - `inventory_batches.quantity`
  - `inventory.quantity`
  - `stock_transactions`
  - `inventory_adjustments`
- Create increase adjustment through API updates quantities and creates no P/L loss.
- Decrease over available stock returns conflict/problem.
- Staff create attempt forbidden.
- Staff can list history.
- Owner can void adjustment and quantities reverse.
- Voided adjustment excluded from profit/loss.
- Profit/loss includes adjustment row.
- Dashboard includes adjustment loss.
- Other-shop isolation for create/list/void/report.

### Frontend Tests

- `InventoryService` methods for adjust, list history, void adjustment.
- Batch page opens adjust dialog with selected batch.
- Direction filters reason dropdown.
- Quantity precision/positive validation.
- Notes required for `OtherLoss`/`OtherGain`.
- Preview calculates before/after and loss impact.
- Successful adjustment refreshes batches.
- Adjustment history loads paged data and applies filters.
- New adjustment from history requires/selects batch.
- Owner sees void action; non-owner does not.
- Profit/loss page renders adjustment row type.
- Dashboard tests updated for wastage/profit expectations.
- i18n coverage tests include new keys.

## Out Of Scope For V1

- CSV export.
- Attachments/photos/documents.
- Bulk stock count/adjustment workflow.
- Creating a new batch from adjustment flow.
- Supplier credit note or supplier return ledger integration.
- Automatic expense creation.
- Approval workflow.

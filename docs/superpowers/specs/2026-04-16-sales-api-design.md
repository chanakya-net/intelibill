# Sales API Design

**Date:** 2026-04-16  
**Scope:** Backend only — POST /api/sales endpoint with domain, application, infrastructure, and test layers.

---

## Context

Intelibill is a multi-tenant inventory management system. The inventory inbound flow (AddInventory) already exists: items + batches + stock transactions. Sales is the mirror: deduct from batches/inventory, record a sale with customer and payment details.

No Sale entity exists yet. This spec introduces the full sales foundation.

---

## Requirements Summary

- Receive one or more sale line items in a single request (same customer for all)
- Each line item: barcode, batch number, item name, quantity, cost price, sales price, MRP, tax rate %, is price including tax
- Validate each line item against DB records before processing
- Customer: walk-in (name + phone, no DB record) or regular (CustomerId FK to customers table) — customer-type logic deferred to later
- Payment method: fixed enum (Cash, UPI, Card, Credit)
- Invoice number: auto-generated server-side (`INV-YYYYMMDD-XXXXXXXX`)
- Price mismatch (vs batch record): warn in response, do not block sale
- Multi-item: single DB round trip (one SaveChangesAsync)
- Entries in: sales, sale_items, inventory_batches (quantity deducted), inventory (aggregate deducted), stock_transactions (Out)

---

## Domain Layer

### New Enum: `PaymentMethod`

```
Cash = 1
UPI = 2
Card = 3
Credit = 4
```

### New Entity: `Sale`

Aggregate root. Shop-scoped.

| Field | Type | Constraints |
|---|---|---|
| Id | Guid | PK |
| ShopId | Guid | FK → shops |
| InvoiceNumber | string(40) | Unique per shop. Format: `INV-YYYYMMDD-XXXXXXXX` |
| CustomerId | Guid? | Nullable FK → customers |
| CustomerName | string?(180) | Walk-in name |
| CustomerPhone | string?(32) | Walk-in phone |
| PaymentMethod | PaymentMethod | enum |
| SoldAt | DateTimeOffset | UTC, set server-side |
| TotalAmount | decimal(18,2) | Sum of quantity × sales_price per item |
| TotalTaxAmount | decimal(18,2) | Computed at handler from TaxRatePercent |
| Items | List\<SaleItem\> | Navigation |

Factory: `Sale.Create(shopId, invoiceNumber, customerId, customerName, customerPhone, paymentMethod, soldAt, totalAmount, totalTaxAmount)`

### New Entity: `SaleItem`

Child of Sale. Not an independent aggregate.

| Field | Type | Constraints |
|---|---|---|
| Id | Guid | PK |
| SaleId | Guid | FK → sales |
| ShopId | Guid | For RLS consistency |
| ItemId | Guid | FK → items |
| InventoryBatchId | Guid | FK → inventory_batches |
| Quantity | decimal(18,3) | > 0 |
| CostPrice | decimal(18,2) | >= 0 |
| SalesPrice | decimal(18,2) | >= 0 |
| Mrp | decimal(18,2) | >= 0 |
| TaxRatePercent | decimal(5,2) | 0–100 |
| IsPriceIncludingTax | bool | |
| HasPriceMismatch | bool | True if any price differs from batch record |

Factory: `SaleItem.Create(...)`

### New Repository Interface: `ISaleRepository`

```csharp
Task AddAsync(Sale sale, CancellationToken cancellationToken);
Task<Sale?> GetByIdAsync(Guid saleId, Guid shopId, CancellationToken cancellationToken);
```

---

## Application Layer

### Command

```csharp
RecordSaleCommand(
    Guid ActorUserId,
    Guid ShopId,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    PaymentMethod PaymentMethod,
    IReadOnlyList<RecordSaleItemCommand> Items)

RecordSaleItemCommand(
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax)
```

### Validator Rules

- `Items` must not be empty
- Per item: Barcode not empty, BatchNumber not empty, Quantity > 0, CostPrice >= 0, SalesPrice >= 0, Mrp >= 0, TaxRatePercent 0–100

### Handler: `RecordSaleCommandHandler`

Dependencies: `IItemRepository`, `IInventoryBatchRepository`, `IInventoryRepository`, `IStockTransactionRepository`, `ISaleRepository`, `IUnitOfWork`

**Steps (all within one transaction):**

1. Bulk fetch items: 1 query for all barcodes in request scoped to ShopId
2. Bulk fetch batches: 1 query for all (itemId, batchNumber) pairs
3. Bulk fetch inventory aggregates: 1 query for all itemIds
4. Per line item — fail entire request if:
   - Barcode not found in fetched items
   - Item name mismatch (warn only, do not fail)
   - Batch not found for that item
   - Batch is voided
   - Requested quantity > batch available quantity
5. Set `HasPriceMismatch = true` per item if any of CostPrice/SalesPrice/Mrp/TaxRatePercent differ from batch record
6. Mutate `InventoryBatch.SubtractQuantity(quantity)` per item
7. Mutate `Inventory.SubtractQuantity(quantity)` per item
8. Create `StockTransaction(Out)` per item with `ReferenceNumber = invoiceNumber`
9. Generate invoice number: `INV-{yyyyMMdd}-{Guid.NewGuid():N[..8].ToUpper()}`
10. Compute `TotalAmount = sum(quantity × salesPrice)`, `TotalTaxAmount = sum(computedTaxAmount)`
    - If IsPriceIncludingTax: `taxAmount = qty × salesPrice × rate / (100 + rate)`
    - Else: `taxAmount = qty × salesPrice × rate / 100`
11. Build `Sale` + `SaleItem[]`
12. `await saleRepository.AddAsync(sale)`
13. `await unitOfWork.SaveChangesAsync()`

**Returns:** `ErrorOr<SaleDto>`

### DTOs

```csharp
SaleDto(
    Guid SaleId,
    string InvoiceNumber,
    PaymentMethod PaymentMethod,
    DateTimeOffset SoldAt,
    decimal TotalAmount,
    decimal TotalTaxAmount,
    IReadOnlyList<SaleItemDto> Items,
    IReadOnlyList<string> Warnings)

SaleItemDto(
    Guid SaleItemId,
    Guid ItemId,
    Guid InventoryBatchId,
    decimal Quantity,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool HasPriceMismatch)
```

---

## Infrastructure Layer

### Migration: `AddSalesFoundation`

**Table: `sales`**
- PK: `id`
- FK: `shop_id → shops`
- FK: `customer_id → customers` (nullable)
- UK: `(shop_id, invoice_number)`
- Columns: `customer_name`, `customer_phone`, `payment_method` (int), `sold_at`, `total_amount`, `total_tax_amount`, audit fields

**Table: `sale_items`**
- PK: `id`
- FK: `sale_id → sales`
- FK: `item_id → items` (with `shop_id`)
- FK: `inventory_batch_id → inventory_batches`
- Columns: `shop_id`, `quantity`, `cost_price`, `sales_price`, `mrp`, `tax_rate_percent`, `is_price_including_tax`, `has_price_mismatch`, audit fields

### Repository: `SaleRepository`

EF Core implementation of `ISaleRepository`. Registered in `DependencyInjection.cs`.

### DbContext

Add `DbSet<Sale> Sales` and `DbSet<SaleItem> SaleItems` to `ApplicationDbContext`.

---

## API Layer

### Endpoint

```
POST /api/sales
[Authorize(Policy = "OwnerOrManager")]
```

**Request:**
```csharp
RecordSaleRequest(
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    PaymentMethod PaymentMethod,
    IReadOnlyList<RecordSaleItemRequest> Items)

RecordSaleItemRequest(
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax)
```

**Success response:** `201 Created` with `SaleDto`

**Error responses:** follow existing `ToActionResult` pattern via ErrorOr

### Controller: `SalesController`

Route: `api/sales`. Follows exact pattern of `CustomersController` / `InventoryController`.

---

## Test Plan

### Unit — `RecordSaleCommandHandlerTests`

| Scenario | Expected |
|---|---|
| Valid single item, no price mismatch | Sale created, stock deducted, transaction written, no warnings |
| Valid multi-item | Single `SaveChangesAsync` call asserted via NSubstitute |
| Barcode not found | `ErrorOr` error returned |
| Batch not found | `ErrorOr` error returned |
| Batch is voided | `ErrorOr` error returned |
| Quantity exceeds batch stock | `ErrorOr` error returned |
| Price mismatch on one item | Sale proceeds, `HasPriceMismatch = true`, warning in `SaleDto.Warnings` |
| Walk-in customer (no CustomerId) | Accepted, CustomerName/Phone stored |
| Regular customer (CustomerId set) | Accepted, FK stored |
| Tax included calculation | `TotalTaxAmount` computed correctly |
| Tax excluded calculation | `TotalTaxAmount` computed correctly |

### Unit — `RecordSaleCommandValidatorTests`

Empty items list, negative quantity, zero quantity, missing barcode, missing batch number, TaxRatePercent out of range.

### Unit — `SalesControllerTests`

Missing JWT sub claim → 401, missing active_shop_id → 400, valid request → 201, handler error → mapped HTTP status.

### Integration — `SalesIntegrationTests`

Full end-to-end: seed item + batch + inventory → POST /api/sales → assert sale rows, inventory deducted, stock transaction written.

---

## Out of Scope (This Spec)

- Walk-in vs regular customer special logic (deferred)
- Sale returns / refunds
- Sales reporting / GET endpoints
- Payment reconciliation
